// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class CSharpFunctionInvoker : ScriptFunctionInvokerBase
    {
        private const string ScriptClassName = "Submission#0";

        private readonly ScriptHost _host;
        private readonly DictionaryJsonConverter _dictionaryJsonConverter = new DictionaryJsonConverter();
        private readonly TraceWriter _fileTraceWriter;
        private readonly FunctionMetadata _functionMetadata;
        private readonly BindingMetadata _trigger;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly FileSystemWatcher _fileWatcher;
        private readonly string _triggerParameterName;
        private readonly bool _omitInputParameter;
        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private MethodInfo _function;
        private FunctionMetadataResolver _metadataResolver;
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private Action _reloadScript;
        private Action _restorePackages;

        private static readonly string[] _watchedFileTypes = { ".cs", ".csx", ".dll", ".exe" };      

        public CSharpFunctionInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata metadata, bool omitInputParameter,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, 
            IFunctionEntryPointResolver functionEntryPointResolver, FunctionAssemblyLoader assemblyLoader)
        {
            _host = host;
            _trigger = trigger;
            _triggerParameterName = trigger.Name;
            _omitInputParameter = omitInputParameter;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _functionMetadata = metadata;
            _functionEntryPointResolver = functionEntryPointResolver;
            _fileWatcher = InitializeFileWatcher(host);
            _assemblyLoader = assemblyLoader;

            if (_host.ScriptConfig.FileLoggingEnabled)
            {
                string logFilePath = Path.Combine(_host.ScriptConfig.RootLogPath, "Function", _functionMetadata.Name);
                _fileTraceWriter = new FileTraceWriter(logFilePath, TraceLevel.Verbose);
            }
            else
            {
                _fileTraceWriter = NullTraceWriter.Instance;
            }

            _metadataResolver = new FunctionMetadataResolver(metadata, _fileTraceWriter);

            _reloadScript = Debounce(ReloadScript);
            _restorePackages = Debounce(RestorePackages);
        }

        private Action Debounce(Action targetAction, int milliseconds = 500)
        {
            Action<object> action = _ => targetAction();
            action = action.Debounce(500);

            return () => action(null);
        }

        private FileSystemWatcher InitializeFileWatcher(ScriptHost host)
        {
            FileSystemWatcher fileWatcher = null;
            if (host.ScriptConfig.FileWatchingEnabled)
            {
                string functionDirectory = Path.GetDirectoryName(_functionMetadata.Source);
                fileWatcher = new FileSystemWatcher(functionDirectory, "*.*")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                fileWatcher.Changed += OnScriptFileChanged;
                fileWatcher.Created += OnScriptFileChanged;
                fileWatcher.Deleted += OnScriptFileChanged;
                fileWatcher.Renamed += OnScriptFileChanged;
            }

            return fileWatcher;
        }

        private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileExtension = Path.GetExtension(e.Name);
            if (_watchedFileTypes.Contains(fileExtension))
            {
                _reloadScript();
            }
            else if (string.Compare("project.json", e.Name, true) == 0)
            {
                _restorePackages();
            }
        }

        private void ReloadScript()
        {
            // Reset cached function
            _function = null;
            _fileTraceWriter.Verbose(string.Format("Script for function '{0}' changed. Reloading.", _functionMetadata.Name));

            _fileTraceWriter.Verbose("Compiling function script.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            Script<object> script = CreateScript();
            ImmutableArray<Diagnostic> compilationResult = script.Compile();

            stopwatch.Stop();
            _fileTraceWriter.Verbose($"Compilation completed ({stopwatch.ElapsedMilliseconds} ms) .");

            foreach (var diagnostic in compilationResult)
            {
                var traceEvent = new TraceEvent(GetTraceLevelFromDiagnostic(diagnostic), diagnostic.ToString());
                _fileTraceWriter.Trace(traceEvent);
            }
        }

        private void RestorePackages()
        {
            _fileTraceWriter.Verbose("Restoring script packages.");

            _metadataResolver.RestorePackagesAsync()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _fileTraceWriter.Verbose("Package restore failed:");
                        _fileTraceWriter.Verbose(t.Exception.ToString());
                        return;
                    }

                    _fileTraceWriter.Verbose("Packages restored.");    
                    _reloadScript();
                });
        }

        public override async Task Invoke(object[] parameters)
        {
            object input = parameters[0];
            TraceWriter traceWriter = (TraceWriter)parameters[1];
            IBinder binder = (IBinder)parameters[2];
            ExecutionContext functionExecutionContext = (ExecutionContext)parameters[3];

            try
            {
                _fileTraceWriter.Verbose("Function started");

                // if there are any binding parameters in the output bindings,
                // parse the input as json to get the binding data
                var bindingData = new Dictionary<string, string>();
                if (_outputBindings.Any(p => p.HasBindingParameters) ||
                    _inputBindings.Any(p => p.HasBindingParameters))
                {
                    bindingData = GetBindingData(input);
                }
                bindingData["InvocationId"] = functionExecutionContext.InvocationId.ToString();

                MethodInfo function = GetFunctionTarget();

                object[] arguments = await BindArgumentsAsync(binder, function, bindingData, parameters);

                object functionResult = function.Invoke(null, arguments);

                if (functionResult is Task)
                {
                    functionResult = await ((Task)functionResult)
                        .ContinueWith(t => GetTaskResult(t));
                }

                var functionOutputs = functionResult as IDictionary<string, object>;

                // If we have a single output binding, directly specify its value
                if (functionResult != null && _outputBindings.Count == 1)
                {
                    var outputBinding = _outputBindings.Single();

                    if (functionOutputs == null || !functionOutputs.ContainsKey(outputBinding.Name))
                    {
                        functionOutputs = new Dictionary<string, object>
                        {
                            {outputBinding.Name, functionResult }
                        };
                    }
                }
                else if (functionOutputs == null)
                {
                    var output = function.GetParameters().Where(p => p.IsOut).ToList();
                    functionOutputs = output.ToDictionary(o => o.Name, o => arguments[o.Position]);
                }

                await ProcessOutputBindingsAsync(_outputBindings, input, binder, bindingData, functionOutputs);

                _fileTraceWriter.Verbose("Function completed (Success)");
            }
            catch (CompilationErrorException ex)
            {
                _fileTraceWriter.Error("Function compilation error");

                foreach (var diagnostic in ex.Diagnostics.Where(d => !d.IsSuppressed))
                {
                    TraceLevel level = GetTraceLevelFromDiagnostic(diagnostic);
                    _fileTraceWriter.Trace(new TraceEvent(level, diagnostic.ToString()));
                }

                _fileTraceWriter.Verbose("Function completed (Failure)");

                throw;
            }
            catch (Exception ex)
            {
                _fileTraceWriter.Error(ex.Message, ex);
                _fileTraceWriter.Verbose("Function completed (Failure)");
                throw;
            }
        }

        private MethodInfo GetFunctionTarget()
        {
            if (_function == null)
            {
                _assemblyLoader.ReleaseContext(_functionMetadata);

                _fileTraceWriter.Verbose("Compiling function script.");
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                Script<object> script = CreateScript();
                var compilation = script.GetCompilation();

                using (var assemblyStream = new MemoryStream())
                {
                    var result = compilation.Emit(assemblyStream);

                    stopwatch.Stop();
                    _fileTraceWriter.Verbose($"Compilation completed ({stopwatch.ElapsedMilliseconds} ms) .");

                    if (!result.Success)
                    {
                        throw new CompilationErrorException("Script compilation failed.", result.Diagnostics);
                    }

                    Assembly assembly = Assembly.Load(assemblyStream.GetBuffer());
                    System.Reflection.TypeInfo scriptType = assembly.DefinedTypes.FirstOrDefault(t => string.Compare(t.Name, ScriptClassName) == 0);

                    _function = _functionEntryPointResolver.GetFunctionEntryPoint(scriptType?.DeclaredMethods.ToList());

                    _assemblyLoader.CreateContext(_functionMetadata, assembly, _metadataResolver);
                }
            }

            return _function;
        }

        private Script<object> CreateScript()
        {
            string code = GetFunctionSource();

            return CSharpScript.Create(code, options: _metadataResolver.FunctionScriptOptions);
        }

        private object GetTaskResult(Task task)
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

        private TraceLevel GetTraceLevelFromDiagnostic(Diagnostic diagnostic)
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

        private async Task<object[]> BindArgumentsAsync(IBinder binder, MethodInfo function, Dictionary<string, string> bindingData, object[] invocationParameters)
        {
            var functionArguments = function.GetParameters();

            object[] inputList = new object[functionArguments.Length];

            foreach (var argument in functionArguments)
            {
                object value = null;
                if (!argument.IsOut)
                {
                    var argumentBinding = _inputBindings.FirstOrDefault(b => b.Name == argument.Name);

                    if (argumentBinding != null)
                    {
                        BindingContext bindingContext = new BindingContext
                        {
                            Binder = binder,
                            BindingData = bindingData,
                            TargetType = argument.ParameterType
                        };

                        await argumentBinding.BindAsync(bindingContext);

                        value = bindingContext.Value;
                    }

                    if (value == null)
                    {
                        value = invocationParameters.FirstOrDefault(p => argument.ParameterType.IsAssignableFrom(p.GetType()));
                    }
                }

                inputList[argument.Position] = value;
            }

            return inputList;
        }

        private static async Task ProcessOutputBindingsAsync(
            Collection<FunctionBinding> outputBindings, object input, IBinder binder, Dictionary<string, string> bindingData,
            IDictionary<string, object> functionOutputs)
        {
            if (outputBindings == null || functionOutputs == null)
            {
                return;
            }

            foreach (FunctionBinding binding in outputBindings)
            {
                // get the output value from the script
                object value = null;
                if (functionOutputs.TryGetValue(binding.Name, out value))
                {
                    object bindingValue = ConvertBindingValue(value);


                    var bindingContext = new BindingContext
                    {
                        Input = input,
                        Binder = binder,
                        BindingData = bindingData,
                        Value = bindingValue
                    };

                    await binding.BindAsync(bindingContext);

                    if (bindingValue is MemoryStream)
                    {
                        ((IDisposable)bindingValue).Dispose();
                    }
                }
            }
        }

        private static object ConvertBindingValue(object value)
        {
            if (value is string)
            {
                byte[] bytes = Encoding.UTF8.GetBytes((string)value);

                return new MemoryStream(bytes);
            }

            return value;
        }

        private string GetFunctionSource()
        {
            string code = null;

            if (File.Exists(_functionMetadata.Source))
            {
                code = File.ReadAllText(_functionMetadata.Source);
            }

            return code ?? string.Empty;
        }
    }
}
