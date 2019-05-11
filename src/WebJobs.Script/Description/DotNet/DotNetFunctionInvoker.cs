// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class DotNetFunctionInvoker : FunctionInvokerBase
    {
        private readonly string _triggerInputName;
        private readonly FunctionMetadata _functionMetadata;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private readonly ICompilationService<IDotNetCompilation> _compilationService;
        private readonly FunctionLoader<MethodInfo> _functionLoader;
        private readonly IMetricsLogger _metricsLogger;

        private FunctionSignature _functionSignature;
        private IFunctionMetadataResolver _metadataResolver;
        private Func<Task> _reloadScript;
        private Action _onReferencesChanged;
        private Action _restorePackages;
        private string[] _watchedFileTypes;
        private int _compilerErrorCount;

        internal DotNetFunctionInvoker(ScriptHost host,
            FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings,
            Collection<FunctionBinding> outputBindings,
            IFunctionEntryPointResolver functionEntryPointResolver,
            ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver> compilationServiceFactory,
            ILoggerFactory loggerFactory,
            IMetricsLogger metricsLogger,
            ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionMetadataResolver metadataResolver = null)
            : base(host, functionMetadata, loggerFactory)
        {
            _metricsLogger = metricsLogger;
            _functionEntryPointResolver = functionEntryPointResolver;
            _metadataResolver = metadataResolver ?? CreateMetadataResolver(host, bindingProviders, functionMetadata, FunctionLogger);
            _compilationService = compilationServiceFactory.CreateService(functionMetadata.Language, _metadataResolver);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _triggerInputName = functionMetadata.Bindings.FirstOrDefault(b => b.IsTrigger).Name;
            _functionMetadata = functionMetadata;

            InitializeFileWatcher();

            _functionLoader = new FunctionLoader<MethodInfo>(CreateFunctionTarget);

            _reloadScript = ReloadScriptAsync;
            _reloadScript = _reloadScript.Debounce();

            _onReferencesChanged = OnReferencesChanged;
            _onReferencesChanged = _onReferencesChanged.Debounce();

            _restorePackages = RestorePackages;
            _restorePackages = _restorePackages.Debounce();
        }

        private static IFunctionMetadataResolver CreateMetadataResolver(ScriptHost host, ICollection<IScriptBindingProvider> bindingProviders,
            FunctionMetadata functionMetadata, ILogger logger)
        {
            return new ScriptFunctionMetadataResolver(functionMetadata.ScriptFile, bindingProviders, logger);
        }

        private void InitializeFileWatcher()
        {
            if (InitializeFileWatcherIfEnabled())
            {
                _watchedFileTypes = ScriptConstants.AssemblyFileTypes
                    .Concat(_compilationService.SupportedFileTypes)
                    .ToArray();
            }
        }

        protected override void OnScriptFileChanged(FileSystemEventArgs e)
        {
            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileExtension = Path.GetExtension(e.Name);
            if (ScriptConstants.AssemblyFileTypes.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
            {
                // As a result of an assembly change, we initiate a full host shutdown
                _onReferencesChanged();
            }
            else if (_watchedFileTypes.Contains(fileExtension))
            {
                _reloadScript();
            }
            else if (string.Compare(DotNetConstants.ProjectFileName, Path.GetFileName(e.Name), StringComparison.OrdinalIgnoreCase) == 0)
            {
                _restorePackages();
            }
        }

        private void OnReferencesChanged()
        {
            string message = "Assembly reference changes detected. Restarting host...";
            FunctionLogger.LogInformation(message);

            Host.Shutdown();
        }

        public override void OnError(Exception ex)
        {
            string error = Utility.FlattenException(ex, s =>
            {
                string baseAssemblyName = Utility.GetAssemblyNameFromMetadata(Metadata, string.Empty);
                if (s != null && s.StartsWith(baseAssemblyName))
                {
                    return Metadata.Name;
                }

                return s;
            });

            TraceError(error);
        }

        private async Task ReloadScriptAsync()
        {
            // Reset cached function
            _functionLoader.Reset();
            _compilerErrorCount = 0;
            LogOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Script for function '{0}' changed. Reloading.", Metadata.Name), LogLevel.Information);

            ImmutableArray<Diagnostic> compilationResult = ImmutableArray<Diagnostic>.Empty;
            FunctionSignature signature = null;

            try
            {
                IDotNetCompilation compilation = await _compilationService.GetFunctionCompilationAsync(Metadata);
                compilationResult = compilation.GetDiagnostics();

                // TODO: Invoking this without the assembly is not supported by all compilations
                signature = compilation.GetEntryPointSignature(_functionEntryPointResolver, null);
                compilationResult = ValidateFunctionBindingArguments(signature, _triggerInputName, _inputBindings, _outputBindings, compilationResult.ToBuilder());
            }
            catch (CompilationErrorException exc)
            {
                compilationResult = AddFunctionDiagnostics(compilationResult.AddRange(exc.Diagnostics));
            }

            TraceCompilationDiagnostics(compilationResult, LogTargets.User);

            bool compilationSucceeded = !compilationResult.Any(d => d.Severity == DiagnosticSeverity.Error);

            LogOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Compilation {0}.", compilationSucceeded ? "succeeded" : "failed"), LogLevel.Information);

            // If the compilation succeeded, AND:
            //  - We haven't cached a function (failed to compile on load), OR
            //  - We're referencing local function types (i.e. POCOs defined in the function) AND Our our function signature has changed
            // Restart our host.
            if (compilationSucceeded &&
                (_functionSignature == null ||
                (_functionSignature.HasLocalTypeReference || !_functionSignature.Equals(signature))))
            {
                await Host.RestartAsync().ConfigureAwait(false);
            }
        }

        internal async Task<MethodInfo> GetFunctionTargetAsync(bool isInvocation = false)
        {
            try
            {
                return await _functionLoader.GetFunctionTargetAsync().ConfigureAwait(false);
            }
            catch (CompilationErrorException exc)
            {
                // on the invocation path we want to log detailed logs and all compilation diagnostics
                var properties = isInvocation ? null : PrimaryHostLogProperties;
                FunctionLogger.Log(LogLevel.Error, 0, properties, exc, (state, ex) => "Function compilation error");
                TraceCompilationDiagnostics(exc.Diagnostics, LogTargets.User, isInvocation);
                throw;
            }
            catch (CompilationServiceException)
            {
                const string message = "Compilation service error";
                FunctionLogger.LogError(message);

                // Compiler errors are often sporadic, so we'll attempt to reset the loader here to avoid
                // caching the compiler error and leaving the function hopelessly broken
                if (++_compilerErrorCount < 3)
                {
                    _functionLoader.Reset();

                    const string resetMessage = "Function loader reset. Failed compilation result will not be cached.";
                    FunctionLogger.LogError(resetMessage);
                }
                throw;
            }
        }

        private void RestorePackages()
        {
            // Kick off the package restore and return.
            // Any errors will be logged in RestorePackagesAsync
            RestorePackagesAsync(true)
                .ContinueWith(t => t.Exception.Handle(e => true), TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        internal async Task RestorePackagesAsync(bool reloadScriptOnSuccess = true)
        {
            LogOnPrimaryHost("Restoring packages.", LogLevel.Information);

            try
            {
                PackageRestoreResult result = await _metadataResolver.RestorePackagesAsync();

                LogOnPrimaryHost("Packages restored.", LogLevel.Information);

                if (reloadScriptOnSuccess)
                {
                    if (!result.IsInitialInstall && result.ReferencesChanged)
                    {
                        LogOnPrimaryHost("Package references have changed.", LogLevel.Information);

                        // If this is not the initial package install and references changed,
                        // shutdown the host, which will cause it to have a clean start and load the new
                        // assembly references
                        _onReferencesChanged();
                    }
                    else
                    {
                        await _reloadScript();
                    }
                }
            }
            catch (Exception exc)
            {
                LogOnPrimaryHost("Package restore failed:", LogLevel.Error);
                LogOnPrimaryHost(exc.ToFormattedString(), LogLevel.Error);
            }
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            MethodInfo function = await GetFunctionTargetAsync(isInvocation: true);

            // Separate system parameters from the actual method parameters
            object[] originalParameters = parameters;
            int actualParameterCount = function.GetParameters().Length;
            parameters = parameters.Take(actualParameterCount).ToArray();

            object result = function.Invoke(null, parameters);

            // after the function executes, we have to copy values back into the original
            // array to ensure object references are maintained (since we took a copy above)
            for (int i = 0; i < parameters.Length; i++)
            {
                originalParameters[i] = parameters[i];
            }

            // unwrap the task
            if (result is Task)
            {
                result = await ((Task)result).ContinueWith(t => GetTaskResult(t), TaskContinuationOptions.ExecuteSynchronously);
            }

            return result;
        }

        private async Task<MethodInfo> CreateFunctionTarget(CancellationToken cancellationToken)
        {
            try
            {
                await VerifyPackageReferencesAsync();

                string eventName = string.Format(MetricEventNames.FunctionCompileLatencyByLanguageFormat, _compilationService.Language);
                using (_metricsLogger.LatencyEvent(eventName, _functionMetadata.Name))
                {
                    IDotNetCompilation compilation = await _compilationService.GetFunctionCompilationAsync(Metadata);

                    DotNetCompilationResult compilationResult = await compilation.EmitAsync(cancellationToken);
                    Assembly assembly = compilationResult.Load(Metadata, _metadataResolver, FunctionLogger);

                    FunctionSignature functionSignature = compilation.GetEntryPointSignature(_functionEntryPointResolver, assembly);

                    ImmutableArray<Diagnostic> bindingDiagnostics = ValidateFunctionBindingArguments(functionSignature, _triggerInputName, _inputBindings, _outputBindings, throwIfFailed: true);
                    TraceCompilationDiagnostics(bindingDiagnostics);

                    _compilerErrorCount = 0;

                    // Set our function entry point signature
                    _functionSignature = functionSignature;

                    return _functionSignature.GetMethod(assembly);
                }
            }
            catch (CompilationErrorException exc)
            {
                ImmutableArray<Diagnostic> diagnostics = AddFunctionDiagnostics(exc.Diagnostics);

                // Here we only need to trace to system logs
                TraceCompilationDiagnostics(diagnostics, LogTargets.System);

                throw new CompilationErrorException(exc.Message, diagnostics);
            }
        }

        private async Task VerifyPackageReferencesAsync()
        {
            try
            {
                if (_metadataResolver.RequiresPackageRestore(Metadata))
                {
                    LogOnPrimaryHost("Package references have been updated.", LogLevel.Information);
                    await RestorePackagesAsync(false);
                }
            }
            catch (Exception exc)
            {
                // There was an issue processing the package references,
                // wrap the exception in a CompilationErrorException and retrow
                LogOnPrimaryHost("Error processing package references.", LogLevel.Error);
                LogOnPrimaryHost(exc.Message, LogLevel.Error);

                throw new CompilationErrorException("Unable to restore packages", ImmutableArray<Diagnostic>.Empty);
            }
        }

        private ImmutableArray<Diagnostic> AddFunctionDiagnostics(ImmutableArray<Diagnostic> diagnostics)
        {
            var result = diagnostics.Aggregate(new List<Diagnostic>(), (a, d) =>
            {
                a.Add(d);
                ImmutableArray<Diagnostic> functionsDiagnostics = GetFunctionDiagnostics(d);

                if (!functionsDiagnostics.IsEmpty)
                {
                    a.AddRange(functionsDiagnostics);
                }

                return a;
            });

            return ImmutableArray.CreateRange(result);
        }

        private ImmutableArray<Diagnostic> GetFunctionDiagnostics(Diagnostic diagnostic)
        {
            // If metadata file not found
            if (string.Compare(diagnostic.Id, "CS0006", StringComparison.OrdinalIgnoreCase) == 0)
            {
                string messagePattern = diagnostic.Descriptor.MessageFormat.ToString().Replace("{0}", "(?<arg>.*)");

                Match match = Regex.Match(diagnostic.GetMessage(), messagePattern);

                PackageReference package;

                // If we have the assembly name argument, and it is a package assembly, add a compilation warning
                if (match.Success && match.Groups["arg"] != null && _metadataResolver.TryGetPackageReference(match.Groups["arg"].Value, out package))
                {
                    string message = string.Format(
                        CultureInfo.InvariantCulture,
                        "The reference '{0}' is part of the referenced NuGet package '{1}'. Package assemblies are automatically referenced by your Function and do not require a '#r' directive.",
                        match.Groups["arg"].Value, package.Name);

                    var descriptor = new DiagnosticDescriptor(
                        DotNetConstants.RedundantPackageAssemblyReference,
                        "Redundant assembly reference", message, "AzureFunctions", DiagnosticSeverity.Warning, true);

                    return ImmutableArray.Create(Diagnostic.Create(descriptor, diagnostic.Location));
                }
            }
            // Check if script compilation failed due to missing assembly (CS0246)
            else if (string.Compare(diagnostic.Id, DotNetConstants.TypeOrNamespaceNotFoundCompilerErrorCode, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // If so, check for extensions.csproj and project.json files. Log warning.
                WarnIfDeprecatedNugetReferenceFound(diagnostic);
            }

            return ImmutableArray<Diagnostic>.Empty;
        }

        internal static ImmutableArray<Diagnostic> ValidateFunctionBindingArguments(FunctionSignature functionSignature, string triggerInputName,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings,
            ImmutableArray<Diagnostic>.Builder builder = null, bool throwIfFailed = false)
        {
            var resultBuilder = builder ?? ImmutableArray<Diagnostic>.Empty.ToBuilder();

            if (!functionSignature.Parameters.Any(p => string.Compare(p.Name, triggerInputName, StringComparison.Ordinal) == 0))
            {
                string message = string.Format(CultureInfo.InvariantCulture, "Missing a trigger argument named '{0}'.", triggerInputName);
                var descriptor = new DiagnosticDescriptor(DotNetConstants.MissingTriggerArgumentCompilationCode,
                    "Missing trigger argument", message, "AzureFunctions", DiagnosticSeverity.Error, true);

                resultBuilder.Add(Diagnostic.Create(descriptor, Location.None));
            }

            var bindings = inputBindings.Where(b => !b.Metadata.IsTrigger).Union(outputBindings);

            foreach (var binding in bindings)
            {
                if (string.Compare("http", binding.Metadata.Type, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    continue;
                }

                if (binding.Metadata.IsReturn)
                {
                    continue;
                }

                if (!functionSignature.Parameters.Any(p => string.Compare(p.Name, binding.Metadata.Name, StringComparison.Ordinal) == 0))
                {
                    string message = string.Format(CultureInfo.InvariantCulture,
                        "Missing binding argument named '{0}'. Mismatched binding argument names may lead to function indexing errors.", binding.Metadata.Name);

                    var descriptor = new DiagnosticDescriptor(DotNetConstants.MissingBindingArgumentCompilationCode,
                        "Missing binding argument", message, "AzureFunctions", DiagnosticSeverity.Warning, true);

                    resultBuilder.Add(Diagnostic.Create(descriptor, Location.None));
                }
            }

            ImmutableArray<Diagnostic> result = resultBuilder.ToImmutable();

            if (throwIfFailed && result.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new CompilationErrorException("Function compilation failed.", result);
            }

            return resultBuilder.ToImmutable();
        }

        internal static object GetTaskResult(Task task)
        {
            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            Type taskType = task.GetType();

            if (taskType.IsGenericType)
            {
                return taskType.GetProperty("Result").GetValue(task);
            }

            return null;
        }

        private void WarnIfDeprecatedNugetReferenceFound(Diagnostic diagnostic)
        {
            string functionDirectory = Metadata.FunctionDirectory;
            string deprecatedProjectPath = Path.Combine(functionDirectory, DotNetConstants.DeprecatedProjectFileName);
            string extensionsProjectPath = Path.Combine(functionDirectory, ScriptConstants.ExtensionsProjectFileName);
            const string warningString = "You may be referencing NuGet packages incorrectly. The file '{0}' should not be used to reference NuGet packages. Try creating a '{1}' file instead. Learn more: https://go.microsoft.com/fwlink/?linkid=2091419";

            if (File.Exists(deprecatedProjectPath))
            {
                string warning = string.Format(warningString, deprecatedProjectPath, DotNetConstants.ProjectFileName);
                FunctionLogger.LogWarning(warning);
            }
            else if (File.Exists(extensionsProjectPath))
            {
                string warning = string.Format(warningString, extensionsProjectPath, DotNetConstants.ProjectFileName);
                FunctionLogger.LogWarning(warning);
            }
            else
            {
                FunctionLogger.LogWarning($"You may be referencing NuGet packages incorrectly. Learn more: https://go.microsoft.com/fwlink/?linkid=2091419");
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _functionLoader.Dispose();
            }
        }
    }
}
