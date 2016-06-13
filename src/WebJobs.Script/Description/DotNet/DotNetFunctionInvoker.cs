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
    public sealed class DotNetFunctionInvoker : FunctionInvokerBase
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private readonly string _triggerInputName;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private readonly IMetricsLogger _metrics;
        private readonly ReaderWriterLockSlim _functionValueLoaderLock = new ReaderWriterLockSlim();
        private readonly ICompilationService _compilationService;

        private FunctionSignature _functionSignature;
        private IFunctionMetadataResolver _metadataResolver;
        private Action _reloadScript;
        private Action _restorePackages;
        private Action<MethodInfo, object[], object[], object> _resultProcessor;
        private FunctionValueLoader _functionValueLoader;
        private string[] _watchedFileTypes;

        private static readonly string[] AssemblyFileTypes = { ".dll", ".exe" };

        internal DotNetFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings,
            IFunctionEntryPointResolver functionEntryPointResolver, FunctionAssemblyLoader assemblyLoader, 
            ICompilationServiceFactory compilationServiceFactory)
            : base(host, functionMetadata)
        {
            _functionEntryPointResolver = functionEntryPointResolver;
            _assemblyLoader = assemblyLoader;
            _metadataResolver = new FunctionMetadataResolver(functionMetadata, host.ScriptConfig.BindingProviders, TraceWriter);
            _compilationService = compilationServiceFactory.CreateService(functionMetadata.ScriptType, _metadataResolver);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _triggerInputName = functionMetadata.Bindings.FirstOrDefault(b => b.IsTrigger).Name;
            _metrics = host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();

            InitializeFileWatcher();

            _resultProcessor = CreateResultProcessor();

            _functionValueLoader = FunctionValueLoader.Create(CreateFunctionTarget);

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

        private void ReloadScript()
        {
            // Reset cached function
            ResetFunctionValue();
            TraceWriter.Info(string.Format(CultureInfo.InvariantCulture, "Script for function '{0}' changed. Reloading.", Metadata.Name));

            TraceWriter.Info("Compiling function script.");

            ICompilation compilation = _compilationService.GetFunctionCompilation(Metadata);
            ImmutableArray<Diagnostic> compilationResult = compilation.GetDiagnostics();

            FunctionSignature signature = compilation.GetEntryPointSignature(_functionEntryPointResolver);
            compilationResult = ValidateFunctionBindingArguments(signature, compilationResult.ToBuilder());

            TraceCompilationDiagnostics(compilationResult);

            bool compilationSucceeded = !compilationResult.Any(d => d.Severity == DiagnosticSeverity.Error);

            TraceWriter.Info(string.Format(CultureInfo.InvariantCulture, "Compilation {0}.",
                compilationSucceeded ? "succeeded" : "failed"));

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

        private void ResetFunctionValue()
        {
            _functionValueLoaderLock.EnterWriteLock();
            try
            {
                if (_functionValueLoader != null)
                {
                    _functionValueLoader.Dispose();
                }

                _functionValueLoader = FunctionValueLoader.Create(CreateFunctionTarget);
            }
            finally
            {
                _functionValueLoaderLock.ExitWriteLock();
            }
        }

        private void RestorePackages()
        {
            TraceWriter.Info("Restoring packages.");

            _metadataResolver.RestorePackagesAsync()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        TraceWriter.Info("Package restore failed:");
                        TraceWriter.Info(t.Exception.ToString());
                        return;
                    }

                    TraceWriter.Info("Packages restored.");
                    _reloadScript();
                });
        }

        public override async Task Invoke(object[] parameters)
        {
            FunctionStartedEvent startedEvent = null;
            string invocationId = null;

            try
            {
                // Separate system parameters from the actual method parameters
                object[] originalParameters = parameters;
                MethodInfo function = await GetFunctionTargetAsync();
                int actualParameterCount = function.GetParameters().Length;
                object[] systemParameters = parameters.Skip(actualParameterCount).ToArray();
                parameters = parameters.Take(actualParameterCount).ToArray();

                ExecutionContext functionExecutionContext = (ExecutionContext)systemParameters[0];
                invocationId = functionExecutionContext.InvocationId.ToString();

                startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
                _metrics.BeginEvent(startedEvent);

                TraceWriter.Info(string.Format("Function started (Id={0})", invocationId));

                parameters = ProcessInputParameters(parameters);

                object functionResult = function.Invoke(null, parameters);

                // after the function executes, we have to copy values back into the original
                // array to ensure object references are maintained (since we took a copy above)
                for (int i = 0; i < parameters.Length; i++)
                {
                    originalParameters[i] = parameters[i];
                }

                if (functionResult is Task)
                {
                    functionResult = await((Task)functionResult).ContinueWith(t => GetTaskResult(t));
                }

                if (functionResult != null)
                {
                    _resultProcessor(function, parameters, systemParameters, functionResult);
                }

                TraceWriter.Info(string.Format("Function completed (Success, Id={0})", invocationId));
            }
            catch
            {
                if (startedEvent != null)
                {
                    startedEvent.Success = false;
                    TraceWriter.Error(string.Format("Function completed (Failure, Id={0})", invocationId));
                }
                else
                {
                    TraceWriter.Error("Function completed (Failure)");
                }
                throw;
            }
            finally
            {
                if (startedEvent != null)
                {
                    _metrics.EndEvent(startedEvent);
                }
            }
        }

        private object[] ProcessInputParameters(object[] parameters)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                TraceWriter writer = parameters[i] as TraceWriter;
                if (writer != null)
                {
                    parameters[i] = new CompositeTraceWriter(new[] { writer, TraceWriter });
                }
            }

            return parameters;
        }

        internal async Task<MethodInfo> GetFunctionTargetAsync(int attemptCount = 0)
        {
            FunctionValueLoader currentValueLoader;
            _functionValueLoaderLock.EnterReadLock();
            try
            {
                currentValueLoader = _functionValueLoader;
            }
            finally
            {
                _functionValueLoaderLock.ExitReadLock();
            }

            try
            {
                return await currentValueLoader;
            }
            catch (OperationCanceledException)
            {
                // If the current task we were awaiting on was cancelled due to a
                // cache refresh, retry, which will use the new loader
                if (attemptCount > 2)
                {
                    throw;
                }
            }

            return await GetFunctionTargetAsync(++attemptCount);
        }

        private MethodInfo CreateFunctionTarget(CancellationToken cancellationToken)
        {
            MemoryStream assemblyStream = null;
            MemoryStream pdbStream = null;

            try
            {
                ICompilation compilation = _compilationService.GetFunctionCompilation(Metadata);
                FunctionSignature functionSignature = compilation.GetEntryPointSignature(_functionEntryPointResolver);

                ValidateFunctionBindingArguments(functionSignature, throwIfFailed: true);

                using (assemblyStream = new MemoryStream())
                {
                    using (pdbStream = new MemoryStream())
                    {
                        compilation.Emit(assemblyStream, pdbStream, cancellationToken);
                
                        // Check if cancellation was requested while we were compiling, 
                        // and if so quit here. 
                        cancellationToken.ThrowIfCancellationRequested();

                        Assembly assembly = Assembly.Load(assemblyStream.GetBuffer(), pdbStream.GetBuffer());
                        _assemblyLoader.CreateOrUpdateContext(Metadata, assembly, _metadataResolver, TraceWriter);

                        // Get our function entry point
                        _functionSignature = functionSignature;
                        System.Reflection.TypeInfo scriptType = assembly.DefinedTypes
                            .FirstOrDefault(t => string.Compare(t.Name, functionSignature.ParentTypeName, StringComparison.Ordinal) == 0);

                        return _functionEntryPointResolver.GetFunctionEntryPoint(scriptType.DeclaredMethods.ToList());
                    }
                }
            }
            catch (CompilationErrorException ex)
            {
                TraceWriter.Error("Function compilation error");
                TraceCompilationDiagnostics(ex.Diagnostics);
                throw;
            }
        }

        private void TraceCompilationDiagnostics(ImmutableArray<Diagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics.Where(d => !d.IsSuppressed))
            {
                TraceLevel level = GetTraceLevelFromDiagnostic(diagnostic);
                TraceWriter.Trace(new TraceEvent(level, diagnostic.ToString()));

                ImmutableArray<Diagnostic> scriptDiagnostics = GetFunctionDiagnostics(diagnostic);

                if (!scriptDiagnostics.IsEmpty)
                {
                    TraceCompilationDiagnostics(scriptDiagnostics);
                }
            }
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

        private ImmutableArray<Diagnostic> ValidateFunctionBindingArguments(FunctionSignature functionSignature,
            ImmutableArray<Diagnostic>.Builder builder = null, bool throwIfFailed = false)
        {
            var resultBuilder = builder ?? ImmutableArray<Diagnostic>.Empty.ToBuilder();

            if (!functionSignature.Parameters.Any(p => string.Compare(p.Name, _triggerInputName, StringComparison.Ordinal) == 0))
            {
                string message = string.Format(CultureInfo.InvariantCulture, "Missing a trigger argument named '{0}'.", _triggerInputName);
                var descriptor = new DiagnosticDescriptor(DotNetConstants.MissingTriggerArgumentCompilationCode,
                    "Missing trigger argument", message, "AzureFunctions", DiagnosticSeverity.Error, true);

                resultBuilder.Add(Diagnostic.Create(descriptor, Location.None));
            }

            var bindings = _inputBindings.Where(b => !b.Metadata.IsTrigger).Union(_outputBindings);

            foreach (var binding in bindings)
            {
                if (string.Compare("http", binding.Metadata.Type, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    continue;
                }

                if (!functionSignature.Parameters.Any(p => string.Compare(p.Name, binding.Metadata.Name, StringComparison.Ordinal) == 0))
                {
                    string message = string.Format(CultureInfo.InvariantCulture, "Missing binding argument named '{0}'.", binding.Metadata.Name);
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

        private Action<MethodInfo, object[], object[], object> CreateResultProcessor()
        {
            var bindings = _inputBindings.Union(_outputBindings).OfType<IResultProcessingBinding>();

            Action<MethodInfo, object[], object[], object> processor = null;
            if (bindings.Any())
            {
                processor = (function, args, systemArgs, result) =>
                {
                    // Find the binding parameter input by
                    // checking if we have the raw value (passed as the DefaultSystemTriggerParameterName)
                    // or getting the function input parameter
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

        private static TraceLevel GetTraceLevelFromDiagnostic(Diagnostic diagnostic)
        {
            var level = TraceLevel.Off;
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Hidden:
                    level = TraceLevel.Verbose;
                    break;
                case DiagnosticSeverity.Info:
                    level = TraceLevel.Info;
                    break;
                case DiagnosticSeverity.Warning:
                    level = TraceLevel.Warning;
                    break;
                case DiagnosticSeverity.Error:
                    level = TraceLevel.Error;
                    break;
            }

            return level;
        }
    }
}
