// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class CSharpFunctionInvoker : ScriptFunctionInvokerBase
    {
        private const string ScriptClassName = "Submission#0";

        private readonly FunctionAssemblyLoader _assemblyLoader;
        private readonly ScriptHost _host;
        private readonly string _triggerInputName;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private readonly IMetricsLogger _metrics;
        private readonly ReaderWriterLockSlim _functionValueLoaderLock = new ReaderWriterLockSlim();

        private CSharpFunctionSignature _functionSignature;
        private FunctionMetadataResolver _metadataResolver;
        private Action _reloadScript;
        private Action _restorePackages;
        private Action<MethodInfo, object[], object> _resultProcessor;
        private FunctionValueLoader _functionValueLoader;

        private static readonly string[] WatchedFileTypes = { ".cs", ".csx", ".dll", ".exe" };

        internal CSharpFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings,
            IFunctionEntryPointResolver functionEntryPointResolver, FunctionAssemblyLoader assemblyLoader)
            : base(host, functionMetadata)
        {
            _host = host;
            _functionEntryPointResolver = functionEntryPointResolver;
            _assemblyLoader = assemblyLoader;
            _metadataResolver = new FunctionMetadataResolver(functionMetadata, TraceWriter);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _triggerInputName = GetTriggerInputName(functionMetadata);
            _metrics = host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();

            InitializeFileWatcherIfEnabled();
            _resultProcessor = CreateResultProcessor();

            _functionValueLoader = FunctionValueLoader.Create(CreateFunctionTarget);

            _reloadScript = ReloadScript;
            _reloadScript = _reloadScript.Debounce();

            _restorePackages = RestorePackages;
            _restorePackages = _restorePackages.Debounce();
        }

        // TODO: Is this function still needed? Can we factor it away?
        private static string GetTriggerInputName(FunctionMetadata functionMetadata)
        {
            BindingMetadata triggerBinding = functionMetadata.Bindings.FirstOrDefault(b => b.IsTrigger);

            string triggerName = null;
            if (triggerBinding != null)
            {
                triggerName = triggerBinding.Name;
            }

            return triggerName ?? FunctionDescriptorProvider.DefaultInputParameterName;
        }

        protected override void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileExtension = Path.GetExtension(e.Name);
            if (WatchedFileTypes.Contains(fileExtension))
            {
                _reloadScript();
            }
            else if (string.Compare(CSharpConstants.ProjectFileName, e.Name, StringComparison.OrdinalIgnoreCase) == 0)
            {
                _restorePackages();
            }
        }

        private void ReloadScript()
        {
            // Reset cached function
            ResetFunctionValue();
            TraceWriter.Verbose(string.Format(CultureInfo.InvariantCulture, "Script for function '{0}' changed. Reloading.", Metadata.Name));

            TraceWriter.Verbose("Compiling function script.");

            Script<object> script = CreateScript();
            Compilation compilation = script.GetCompilation();
            ImmutableArray<Diagnostic> compilationResult = compilation.GetDiagnostics();

            CSharpFunctionSignature signature = CSharpFunctionSignature.FromCompilation(compilation, _functionEntryPointResolver);
            compilationResult = ValidateFunctionBindingArguments(signature, compilationResult.ToBuilder());

            TraceCompilationDiagnostics(compilationResult);

            bool compilationSucceeded = !compilationResult.Any(d => d.Severity == DiagnosticSeverity.Error);

            TraceWriter.Verbose(string.Format(CultureInfo.InvariantCulture, "Compilation {0}.",
                compilationSucceeded ? "succeeded" : "failed"));

            // If the compilation succeeded, AND:
            //  - We haven't cached a function (failed to compile on load), OR
            //  - We're referencing local function types (i.e. POCOs defined in the function) AND Our our function signature has changed
            // Restart our host.
            if (compilationSucceeded &&
                (_functionSignature == null ||
                (_functionSignature.HasLocalTypeReference || !_functionSignature.Equals(signature))))
            {
                _host.RestartEvent.Set();
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
            TraceWriter.Verbose("Restoring packages.");

            _metadataResolver.RestorePackagesAsync()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        TraceWriter.Verbose("Package restore failed:");
                        TraceWriter.Verbose(t.Exception.ToString());
                        return;
                    }

                    TraceWriter.Verbose("Packages restored.");
                    _reloadScript();
                });
        }

        public override async Task Invoke(object[] parameters)
        {
            // Separate system parameters from the actual method parameters
            object[] originalParameters = parameters;
            MethodInfo function = await GetFunctionTargetAsync();
            int actualParameterCount = function.GetParameters().Length;
            object[] systemParameters = parameters.Skip(actualParameterCount).ToArray();
            parameters = parameters.Take(actualParameterCount).ToArray();
            
            ExecutionContext functionExecutionContext = (ExecutionContext)systemParameters[0];
            string invocationId = functionExecutionContext.InvocationId.ToString();

            FunctionStartedEvent startedEvent = new FunctionStartedEvent(Metadata);
            _metrics.BeginEvent(startedEvent);

            try
            {
                TraceWriter.Verbose(string.Format("Function started (Id={0})", invocationId));

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
                    _resultProcessor(function, parameters, functionResult);
                }

                TraceWriter.Verbose(string.Format("Function completed (Success, Id={0})", invocationId));
            }
            catch (Exception ex)
            {
                TraceWriter.Error(ex.Message, ex is CompilationErrorException ? null : ex);

                startedEvent.Success = false;
                TraceWriter.Error(ex.Message, ex);

                TraceWriter.Verbose(string.Format("Function completed (Failure, Id={0})", invocationId));
                throw;
            }
            finally
            {
                _metrics.EndEvent(startedEvent);
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
            // TODO:Get this from some context set in/by the host.
            bool debug = true;
            MemoryStream assemblyStream = null;
            MemoryStream pdbStream = null;

            try
            {
                Script<object> script = CreateScript();
                Compilation compilation = GetScriptCompilation(script, debug);
                CSharpFunctionSignature functionSignature = CSharpFunctionSignature.FromCompilation(compilation, _functionEntryPointResolver);

                ValidateFunctionBindingArguments(functionSignature, throwIfFailed: true);

                using (assemblyStream = new MemoryStream())
                {
                    using (pdbStream = new MemoryStream())
                    {
                        var result = compilation.Emit(assemblyStream, pdbStream);

                        // Check if cancellation was requested while we were compiling, 
                        // and if so quit here. 
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!result.Success)
                        {
                            throw new CompilationErrorException("Script compilation failed.", result.Diagnostics);
                        }

                        Assembly assembly = Assembly.Load(assemblyStream.GetBuffer(), pdbStream.GetBuffer());
                        _assemblyLoader.CreateOrUpdateContext(Metadata, assembly, _metadataResolver);

                        // Get our function entry point
                        System.Reflection.TypeInfo scriptType = assembly.DefinedTypes.FirstOrDefault(t => string.Compare(t.Name, ScriptClassName, StringComparison.Ordinal) == 0);
                        _functionSignature = functionSignature;
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
            }
        }

        private ImmutableArray<Diagnostic> ValidateFunctionBindingArguments(CSharpFunctionSignature functionSignature,
            ImmutableArray<Diagnostic>.Builder builder = null, bool throwIfFailed = false)
        {
            var resultBuilder = builder ?? ImmutableArray<Diagnostic>.Empty.ToBuilder();

            if (!functionSignature.Parameters.Any(p => string.Compare(p.Name, _triggerInputName, StringComparison.Ordinal) == 0))
            {
                string message = string.Format(CultureInfo.InvariantCulture, "Missing a trigger argument named '{0}'.", _triggerInputName);
                var descriptor = new DiagnosticDescriptor(CSharpConstants.MissingTriggerArgumentCompilationCode,
                    "Missing trigger argument", message, "AzureFunctions", DiagnosticSeverity.Error, true);

                resultBuilder.Add(Diagnostic.Create(descriptor, Location.None));
            }

            var bindings = _inputBindings.Where(b => !b.IsTrigger).Union(_outputBindings);

            foreach (var binding in bindings)
            {
                if (binding.Type == BindingType.Http)
                {
                    continue;
                }

                if (!functionSignature.Parameters.Any(p => string.Compare(p.Name, binding.Name, StringComparison.Ordinal) == 0))
                {
                    string message = string.Format(CultureInfo.InvariantCulture, "Missing binding argument named '{0}'.", binding.Name);
                    var descriptor = new DiagnosticDescriptor(CSharpConstants.MissingBindingArgumentCompilationCode,
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

        private Compilation GetScriptCompilation(Script<object> script, bool debug)
        {
            Compilation compilation = script.GetCompilation();

            OptimizationLevel compilationOptimizationLevel = OptimizationLevel.Release;
            if (debug)
            {
                SyntaxTree scriptTree = compilation.SyntaxTrees.First();
                scriptTree = SyntaxFactory.SyntaxTree(scriptTree.GetRoot(),
                      encoding: Encoding.UTF8,
                      path: Path.GetFileName(Metadata.Source),
                      options: new CSharpParseOptions(kind: SourceCodeKind.Script));

                compilationOptimizationLevel = OptimizationLevel.Debug;

                compilation = compilation
                    .RemoveAllSyntaxTrees()
                    .AddSyntaxTrees(scriptTree);
            }

            return compilation.WithOptions(compilation.Options.WithOptimizationLevel(compilationOptimizationLevel))
                .WithAssemblyName(FunctionAssemblyLoader.GetAssemblyNameFromMetadata(Metadata, compilation.AssemblyName));
        }

        private Script<object> CreateScript()
        {
            string code = GetFunctionSource();
            return CSharpScript.Create(code, options: _metadataResolver.FunctionScriptOptions);
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

        private Action<MethodInfo, object[], object> CreateResultProcessor()
        {
            var bindings = _inputBindings.Union(_outputBindings).OfType<IResultProcessingBinding>();

            Action<MethodInfo, object[], object> processor = null;
            if (bindings.Any())
            {
                processor = (function, args, result) =>
                {
                    ParameterInfo parameter = function.GetParameters()
                    .FirstOrDefault(p => string.Compare(p.Name, _triggerInputName, StringComparison.Ordinal) == 0);

                    if (parameter != null)
                    {
                        foreach (var processingBinding in bindings)
                        {
                            if (processingBinding.CanProcessResult(result))
                            {
                                processingBinding.ProcessResult(args[parameter.Position], result);
                                break;
                            }
                        }
                    }
                };
            }

            return processor ?? ((_, __, ___) => { /*noop*/ });
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

        private string GetFunctionSource()
        {
            string code = null;

            if (File.Exists(Metadata.Source))
            {
                code = File.ReadAllText(Metadata.Source);
            }

            return code ?? string.Empty;
        }
    }
}
