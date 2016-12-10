// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [CLSCompliant(false)]
    public sealed class DotNetFunctionInvoker : FunctionInvokerBase
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private readonly string _triggerInputName;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private readonly ICompilationService _compilationService;
        private readonly FunctionLoader<MethodInfo> _functionLoader;
        private readonly IMetricsLogger _metricsLogger;

        private FunctionSignature _functionSignature;
        private IFunctionMetadataResolver _metadataResolver;
        private Action _reloadScript;
        private Action _restorePackages;
        private Action<MethodInfo, object[], object[], object> _resultProcessor;
        private string[] _watchedFileTypes;

        private static readonly string[] AssemblyFileTypes = { ".dll", ".exe" };

        internal DotNetFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings,
            IFunctionEntryPointResolver functionEntryPointResolver, FunctionAssemblyLoader assemblyLoader,
            ICompilationServiceFactory compilationServiceFactory, ITraceWriterFactory traceWriterFactory = null)
            : base(host, functionMetadata, traceWriterFactory)
        {
            _metricsLogger = Host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();
            _functionEntryPointResolver = functionEntryPointResolver;
            _assemblyLoader = assemblyLoader;
            _metadataResolver = new FunctionMetadataResolver(functionMetadata, host.ScriptConfig.BindingProviders, TraceWriter);
            _compilationService = compilationServiceFactory.CreateService(functionMetadata.ScriptType, _metadataResolver);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _triggerInputName = functionMetadata.Bindings.FirstOrDefault(b => b.IsTrigger).Name;

            InitializeFileWatcher();

            _resultProcessor = CreateResultProcessor();

            _functionLoader = new FunctionLoader<MethodInfo>(CreateFunctionTarget);

            _reloadScript = ReloadScript;
            _reloadScript = _reloadScript.Debounce();

            _restorePackages = RestorePackages;
            _restorePackages = _restorePackages.Debounce();
        }

        private void InitializeFileWatcher()
        {
            if (InitializeFileWatcherIfEnabled())
            {
                _watchedFileTypes = AssemblyFileTypes
                    .Concat(_compilationService.SupportedFileTypes)
                    .ToArray();
            }
        }

        protected override void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileExtension = Path.GetExtension(e.Name);
            if (_watchedFileTypes.Contains(fileExtension))
            {
                _reloadScript();
            }
            else if (string.Compare(DotNetConstants.ProjectFileName, e.Name, StringComparison.OrdinalIgnoreCase) == 0)
            {
                _restorePackages();
            }
        }

        public override void OnError(Exception ex)
        {
            string error = Utility.FlattenException(ex, s =>
            {
                string baseAssemblyName = FunctionAssemblyLoader.GetAssemblyNameFromMetadata(Metadata, string.Empty);
                if (s != null && s.StartsWith(baseAssemblyName))
                {
                    return Metadata.Name;
                }

                return s;
            });

            TraceError(error);
        }

        private void ReloadScript()
        {
            // Reset cached function
            _functionLoader.Reset();
            TraceOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Script for function '{0}' changed. Reloading.", Metadata.Name), TraceLevel.Info);

            ImmutableArray<Diagnostic> compilationResult = ImmutableArray<Diagnostic>.Empty;
            FunctionSignature signature = null;

            try
            {
                ICompilation compilation = _compilationService.GetFunctionCompilation(Metadata);
                compilationResult = compilation.GetDiagnostics();

                signature = compilation.GetEntryPointSignature(_functionEntryPointResolver);
                compilationResult = ValidateFunctionBindingArguments(signature, _triggerInputName, _inputBindings, _outputBindings, compilationResult.ToBuilder());
            }
            catch (CompilationErrorException exc)
            {
                compilationResult = AddFunctionDiagnostics(compilationResult.AddRange(exc.Diagnostics));
            }

            TraceCompilationDiagnostics(compilationResult, suppressSystemLogs: true);

            bool compilationSucceeded = !compilationResult.Any(d => d.Severity == DiagnosticSeverity.Error);

            TraceOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Compilation {0}.", compilationSucceeded ? "succeeded" : "failed"), TraceLevel.Info);

            // If the compilation succeeded, AND:
            //  - We haven't cached a function (failed to compile on load), OR
            //  - We're referencing local function types (i.e. POCOs defined in the function) AND Our our function signature has changed
            // Restart our host.
            if (compilationSucceeded &&
                (_functionSignature == null ||
                (_functionSignature.HasLocalTypeReference || !_functionSignature.Equals(signature))))
            {
                Host.RestartEvent.Set();
            }
        }

        internal async Task<MethodInfo> GetFunctionTargetAsync()
        {
            try
            {
                return await _functionLoader.GetFunctionTargetAsync().ConfigureAwait(false);
            }
            catch (CompilationErrorException exc)
            {
                TraceOnPrimaryHost("Function compilation error", TraceLevel.Error);
                TraceCompilationDiagnostics(exc.Diagnostics, suppressSystemLogs: true);
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

        private async Task RestorePackagesAsync(bool reloadScriptOnSuccess = true)
        {
            TraceOnPrimaryHost("Restoring packages.", TraceLevel.Info);

            try
            {
                await _metadataResolver.RestorePackagesAsync();

                TraceOnPrimaryHost("Packages restored.", TraceLevel.Info);

                if (reloadScriptOnSuccess)
                {
                    _reloadScript();
                }
            }
            catch (Exception exc)
            {
                TraceOnPrimaryHost("Package restore failed:", TraceLevel.Error);
                TraceOnPrimaryHost(exc.ToString(), TraceLevel.Error);
            }
        }

        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            // Separate system parameters from the actual method parameters
            object[] originalParameters = parameters;
            MethodInfo function = await GetFunctionTargetAsync();

            int actualParameterCount = function.GetParameters().Length;
            object[] systemParameters = parameters.Skip(actualParameterCount).ToArray();
            parameters = parameters.Take(actualParameterCount).ToArray();

            parameters = ProcessInputParameters(parameters);

            object result = function.Invoke(null, parameters);

            // after the function executes, we have to copy values back into the original
            // array to ensure object references are maintained (since we took a copy above)
            for (int i = 0; i < parameters.Length; i++)
            {
                originalParameters[i] = parameters[i];
            }

            if (result is Task)
            {
                result = await ((Task)result).ContinueWith(t => GetTaskResult(t), TaskContinuationOptions.ExecuteSynchronously);
            }

            if (result != null)
            {
                _resultProcessor(function, parameters, systemParameters, result);
            }

            // if a return value binding was specified, copy the return value
            // into the output binding slot (by convention the last parameter)
            var returnValueBinding = Metadata.Bindings.SingleOrDefault(p => p.IsReturn);
            if (returnValueBinding != null && !(returnValueBinding is IResultProcessingBinding))
            {
                originalParameters[originalParameters.Length - 1] = result;
            }
        }

        private object[] ProcessInputParameters(object[] parameters)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                TraceWriter writer = parameters[i] as TraceWriter;
                if (writer != null)
                {
                    parameters[i] = CreateUserTraceWriter(writer);
                }
            }

            return parameters;
        }

        private async Task<MethodInfo> CreateFunctionTarget(CancellationToken cancellationToken)
        {
            try
            {
                await VerifyPackageReferencesAsync();

                string eventName = string.Format(MetricEventNames.FunctionCompileLatencyByLanguageFormat, _compilationService.Language);
                using (_metricsLogger.LatencyEvent(eventName))
                {
                    ICompilation compilation = _compilationService.GetFunctionCompilation(Metadata);
                    FunctionSignature functionSignature = compilation.GetEntryPointSignature(_functionEntryPointResolver);

                    ImmutableArray<Diagnostic> bindingDiagnostics = ValidateFunctionBindingArguments(functionSignature, _triggerInputName, _inputBindings, _outputBindings, throwIfFailed: true);
                    TraceCompilationDiagnostics(bindingDiagnostics);

                    Assembly assembly = compilation.EmitAndLoad(cancellationToken);
                    _assemblyLoader.CreateOrUpdateContext(Metadata, assembly, _metadataResolver, TraceWriter);

                    // Get our function entry point
                    _functionSignature = functionSignature;
                    System.Reflection.TypeInfo scriptType = assembly.DefinedTypes
                        .FirstOrDefault(t => string.Compare(t.Name, functionSignature.ParentTypeName, StringComparison.Ordinal) == 0);

                    return _functionEntryPointResolver.GetFunctionEntryPoint(scriptType.DeclaredMethods.ToList());
                }
            }
            catch (CompilationErrorException exc)
            {
                ImmutableArray<Diagnostic> diagnostics = AddFunctionDiagnostics(exc.Diagnostics);
                
                // Here we only need to trace to system logs
                TraceCompilationDiagnostics(diagnostics, suppressUserLogs: true);

                throw new CompilationErrorException(exc.Message, diagnostics);
            }
        }

        private async Task VerifyPackageReferencesAsync()
        {
            try
            {
                if (_metadataResolver.RequiresPackageRestore(Metadata))
                {
                    TraceOnPrimaryHost("Package references have been updated.", TraceLevel.Info);
                    await RestorePackagesAsync(false);
                }
            }
            catch (Exception exc)
            {
                // There was an issue processing the package references,
                // wrap the exception in a CompilationErrorException and retrow
                TraceOnPrimaryHost("Error processing package references.", TraceLevel.Error);
                TraceOnPrimaryHost(exc.Message, TraceLevel.Error);

                throw new CompilationErrorException("Unable to restore packages", ImmutableArray<Diagnostic>.Empty);
            }
        }

        internal void TraceCompilationDiagnostics(ImmutableArray<Diagnostic> diagnostics, bool suppressSystemLogs = false, bool suppressUserLogs = false)
        {
            if (suppressSystemLogs && suppressUserLogs)
            {
                return;
            }

            TraceWriter traceWriter  = TraceWriter;
            IDictionary<string, object> properties = PrimaryHostTraceProperties;

            if (suppressUserLogs)
            {
                traceWriter = Host.TraceWriter;
            }
            else if (suppressSystemLogs)
            {
                properties = PrimaryHostUserTraceProperties; 
            }
            
            foreach (var diagnostic in diagnostics.Where(d => !d.IsSuppressed))
            {
                traceWriter.Trace(diagnostic.ToString(), diagnostic.Severity.ToTraceLevel(), properties);
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
                    string message = string.Format(CultureInfo.InvariantCulture,
                        "The reference '{0}' is part of the referenced NuGet package '{1}'. Package assemblies are automatically referenced by your Function and do not require a '#r' directive.",
                        match.Groups["arg"].Value, package.Name);

                    var descriptor = new DiagnosticDescriptor(DotNetConstants.RedundantPackageAssemblyReference,
                       "Redundant assembly reference", message, "AzureFunctions", DiagnosticSeverity.Warning, true);

                    return ImmutableArray.Create(Diagnostic.Create(descriptor, diagnostic.Location));
                }
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

        private static object GetTaskResult(Task task)
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _functionLoader.Dispose();
            }
        }

        private Action<MethodInfo, object[], object[], object> CreateResultProcessor()
        {
            var bindings = _inputBindings.Union(_outputBindings).OfType<IResultProcessingBinding>();

            Action<MethodInfo, object[], object[], object> processor = null;
            if (bindings.Any())
            {
                processor = (function, args, systemArgs, result) =>
                {
                    ParameterInfo[] parameters = function.GetParameters();
                    IDictionary<string, object> functionArguments = parameters.ToDictionary(p => p.Name, p => args[p.Position]);
                    foreach (var processingBinding in bindings)
                    {
                        if (processingBinding.CanProcessResult(result))
                        {
                            processingBinding.ProcessResult(functionArguments, systemArgs, _triggerInputName, result);
                            break;
                        }
                    }
                };
            }

            return processor ?? ((_, __, ___, ____) => { /*noop*/ });
        }
    }
}
