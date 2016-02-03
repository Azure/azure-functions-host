// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EdgeJs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    public class NodeFunctionInvoker : ScriptFunctionInvokerBase
    {
        private Func<object, Task<object>> _scriptFunc;
        private Func<object, Task<object>> _clearRequireCache;
        private static string FunctionTemplate;
        private static string ClearRequireCacheScript;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly string _triggerParameterName;
        private readonly bool _omitInputParameter;
        private readonly string _script;
        private readonly FileSystemWatcher _fileWatcher;
        private readonly ScriptHost _host;
        private readonly DictionaryJsonConverter _dictionaryJsonConverter = new DictionaryJsonConverter();
        private readonly TraceWriter _fileTraceWriter;
        private readonly FunctionMetadata _functionMetadata;
        private readonly BindingMetadata _trigger;

        static NodeFunctionInvoker()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream("Microsoft.Azure.WebJobs.Script.functionTemplate.js")))
            {
                FunctionTemplate = reader.ReadToEnd();
            }
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream("Microsoft.Azure.WebJobs.Script.clearRequireCache.js")))
            {
                ClearRequireCacheScript = reader.ReadToEnd();
            }
        }

        private Func<object, Task<object>> ScriptFunc
        {
            get
            {
                if (_scriptFunc == null)
                {
                    // We delay create the script function so any syntax errors in
                    // the function will be reported to the Dashboard as an invocation
                    // error rather than a host startup error
                    _scriptFunc = Edge.Func(_script);
                }
                return _scriptFunc;
            }
        }

        private Func<object, Task<object>> ClearRequireCacheFunc
        {
            get
            {
                if (_clearRequireCache == null)
                {
                    _clearRequireCache = Edge.Func(ClearRequireCacheScript);
                }
                return _clearRequireCache;
            }
        }

        internal NodeFunctionInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata metadata, bool omitInputParameter, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            _host = host;
            _trigger = trigger;
            _triggerParameterName = trigger.Name;
            _omitInputParameter = omitInputParameter;
            string scriptFilePath = metadata.Source.Replace('\\', '/');
            _script = string.Format(FunctionTemplate, scriptFilePath);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _functionMetadata = metadata;

            if (host.ScriptConfig.FileWatchingEnabled)
            {
                string functionDirectory = Path.GetDirectoryName(scriptFilePath);
                _fileWatcher = new FileSystemWatcher(functionDirectory, "*.*")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnScriptFileChanged;
                _fileWatcher.Created += OnScriptFileChanged;
                _fileWatcher.Deleted += OnScriptFileChanged;
                _fileWatcher.Renamed += OnScriptFileChanged;
            }

            if (_host.ScriptConfig.FileLoggingEnabled)
            {
                string logFilePath = Path.Combine(_host.ScriptConfig.RootLogPath, "Function", _functionMetadata.Name);
                _fileTraceWriter = new FileTraceWriter(logFilePath, TraceLevel.Verbose);
            }
            else
            {
                _fileTraceWriter = NullTraceWriter.Instance;
            }
        }

        public override async Task Invoke(object[] parameters)
        {
            object input = parameters[0];
            TraceWriter traceWriter = (TraceWriter)parameters[1];
            IBinder binder = (IBinder)parameters[2];
            ExecutionContext functionExecutionContext = (ExecutionContext)parameters[3];

            try
            {
                _fileTraceWriter.Verbose(string.Format("Function started"));

                var scriptExecutionContext = CreateScriptExecutionContext(input, traceWriter, _fileTraceWriter, binder, functionExecutionContext);

                // if there are any binding parameters in the output bindings,
                // parse the input as json to get the binding data
                Dictionary<string, string> bindingData = new Dictionary<string, string>();
                if (_outputBindings.Any(p => p.HasBindingParameters) ||
                    _inputBindings.Any(p => p.HasBindingParameters))
                {
                    bindingData = GetBindingData(input);
                }
                bindingData["InvocationId"] = functionExecutionContext.InvocationId.ToString();

                await ProcessInputBindingsAsync(binder, scriptExecutionContext, bindingData);

                object functionResult = await ScriptFunc(scriptExecutionContext);

                // normalize output binding results
                IDictionary<string, object> functionOutputs = null;
                if (functionResult != null && _outputBindings.Count == 1)
                {
                    // if there is only a single output binding allow that binding value
                    // to be specified directly (i.e. normalize output format)
                    var binding = _outputBindings.Single();
                    functionOutputs = functionResult as IDictionary<string, object>;
                    if (functionOutputs == null || !functionOutputs.ContainsKey(binding.Name))
                    {
                        functionOutputs = new Dictionary<string, object>()
                        {
                            { binding.Name, functionResult }
                        };
                    }
                }

                await ProcessOutputBindingsAsync(_outputBindings, input, binder, bindingData, functionOutputs);

                _fileTraceWriter.Verbose(string.Format("Function completed (Success)"));
            }
            catch (Exception ex)
            {
                _fileTraceWriter.Error(ex.Message, ex);
                _fileTraceWriter.Verbose(string.Format("Function completed (Failure)"));
                throw;
            }
        }

        private async Task ProcessInputBindingsAsync(IBinder binder, Dictionary<string, object> executionContext, Dictionary<string, string> bindingData)
        {
            var nonTriggerInputBindings = _inputBindings.Where(p => !p.IsTrigger);
            foreach (var inputBinding in nonTriggerInputBindings)
            {
                string value = null;
                using (MemoryStream stream = new MemoryStream())
                {
                    BindingContext bindingContext = new BindingContext
                    {
                        Binder = binder,
                        BindingData = bindingData,
                        Value = stream
                    };
                    await inputBinding.BindAsync(bindingContext);

                    stream.Seek(0, SeekOrigin.Begin);
                    StreamReader sr = new StreamReader(stream);
                    value = sr.ReadToEnd();
                }

                executionContext[inputBinding.Name] = value;
            }
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
                    if (value.GetType() == typeof(ExpandoObject))
                    {
                        value = JsonConvert.SerializeObject(value);
                    }

                    byte[] bytes = null;
                    if (value.GetType() == typeof(string))
                    {
                        bytes = Encoding.UTF8.GetBytes((string)value);
                    }

                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        BindingContext bindingContext = new BindingContext
                        {
                            Input = input,
                            Binder = binder,
                            BindingData = bindingData,
                            Value = ms
                        };
                        await binding.BindAsync(bindingContext);
                    }
                }
            }
        }

        private void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_scriptFunc == null)
            {
                // we're not loaded yet, so nothing to reload
                return;
            }

            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileName = Path.GetFileName(e.Name);
            if (string.Compare(fileName, "function.json") != 0)
            {
                // one of the script files for this function changed
                // force a reload on next execution
                _scriptFunc = null;

                // clear the node module cache
                ClearRequireCacheFunc(null).Wait();

                _fileTraceWriter.Verbose(string.Format("Script for function '{0}' changed. Reloading.", _functionMetadata.Name));
            }
        }

        private Dictionary<string, object> CreateScriptExecutionContext(object input, TraceWriter traceWriter, TraceWriter fileTraceWriter, IBinder binder, ExecutionContext functionExecutionContext)
        {
            // create a TraceWriter wrapper that can be exposed to Node.js
            var log = (Func<object, Task<object>>)((text) =>
            {
                traceWriter.Verbose((string)text);
                fileTraceWriter.Verbose((string)text);
                return Task.FromResult<object>(null);
            });

            var context = new Dictionary<string, object>()
            {
                { "invocationId", functionExecutionContext.InvocationId },
                { "log", log }
            };

            Type triggerParameterType = input.GetType();
            if (triggerParameterType == typeof(string))
            {
                // if the input is json, convert to a json object
                Dictionary<string, object> jsonObject;
                if (TryDeserializeJsonObject((string)input, out jsonObject))
                {
                    input = jsonObject;
                }
            }
            else if (triggerParameterType == typeof(HttpRequestMessage))
            {
                // convert the request to a json object
                HttpRequestMessage request = (HttpRequestMessage)input;
                var requestObject = CreateRequestObject(request);
                input = requestObject;

                // If this is a WebHook function, the input should be the
                // request body
                HttpBindingMetadata httpBinding = _trigger as HttpBindingMetadata;
                if (httpBinding != null &&
                    !string.IsNullOrEmpty(httpBinding.WebHookReceiver))
                {
                    input = requestObject["body"];

                    // make the entire request object available as well
                    context["req"] = requestObject;
                }
            }

            if (!_omitInputParameter)
            {
                context["input"] = input;
            }

            return context;
        }

        private Dictionary<string, object> CreateRequestObject(HttpRequestMessage request)
        {
            // TODO: need to provide access to remaining request properties
            Dictionary<string, object> inputDictionary = new Dictionary<string, object>();
            inputDictionary["originalUrl"] = request.RequestUri.ToString();
            inputDictionary["method"] = request.Method.ToString().ToUpperInvariant();
            inputDictionary["query"] = request.GetQueryNameValuePairs().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // if the request includes a body, add it to the request object 
            if (request.Content != null && request.Content.Headers.ContentLength > 0)
            {
                string body = request.Content.ReadAsStringAsync().Result;
                MediaTypeHeaderValue contentType = request.Content.Headers.ContentType;
                Dictionary<string, object> jsonObject;
                if (contentType != null && contentType.MediaType == "application/json" &&
                    TryDeserializeJsonObject(body, out jsonObject))
                {
                    // if the content - type of the request is json, deserialize into an object
                    inputDictionary["body"] = jsonObject;
                }
                else
                {
                    inputDictionary["body"] = body;
                }
            }

            return inputDictionary;
        }

        private bool TryDeserializeJsonObject(string json, out Dictionary<string, object> result)
        {
            result = null;

            if (!IsJson(json))
            {
                return false;
            }

            try
            {
                result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json, _dictionaryJsonConverter);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsJson(string input)
        {
            input = input.Trim();
            return (input.StartsWith("{") && input.EndsWith("}"))
                   || (input.StartsWith("[") && input.EndsWith("]"));
        }
    }
}
