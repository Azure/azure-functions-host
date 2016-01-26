// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script
{
    // TODO: make this internal
    public class NodeFunctionInvoker : ScriptFunctionInvokerBase
    {
        private Func<object, Task<object>> _scriptFunc;
        private Func<object, Task<object>> _clearRequireCache;
        private static string FunctionTemplate;
        private static string ClearRequireCacheScript;
        private readonly Collection<Binding> _inputBindings;
        private readonly Collection<Binding> _outputBindings;
        private readonly string _triggerParameterName;
        private readonly string _script;
        private readonly FileSystemWatcher _fileWatcher;
        private readonly string _functionName;
        private readonly ScriptHost _host;
        private readonly DictionaryJsonConverter _dictionaryJsonConverter = new DictionaryJsonConverter();

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

        internal NodeFunctionInvoker(ScriptHost host, string triggerParameterName, FunctionMetadata metadata, Collection<Binding> inputBindings, Collection<Binding> outputBindings)
        {
            _host = host;
            _triggerParameterName = triggerParameterName;
            string scriptFilePath = metadata.Source.Replace('\\', '/');
            _script = string.Format(FunctionTemplate, scriptFilePath);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _functionName = metadata.Name;

            if (host.ScriptConfig.WatchFiles)
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
        }

        public override async Task Invoke(object[] parameters)
        {
            object input = parameters[0];
            TraceWriter traceWriter = (TraceWriter)parameters[1];
            IBinder binder = (IBinder)parameters[2];

            var executionContext = CreateExecutionContext(input, traceWriter, binder);

            // if there are any binding parameters in the output bindings,
            // parse the input as json to get the binding data
            Dictionary<string, string> bindingData = null;
            if (_outputBindings.Any(p => p.HasBindingParameters) ||
                _inputBindings.Any(p => p.HasBindingParameters))
            {
                bindingData = GetBindingData(input);
            }

            await ProcessInputBindingsAsync(binder, executionContext, bindingData);

            // invoke the user code
            object functionResult = null;
            try
            {
                 functionResult = await ScriptFunc(executionContext);
            }
            catch (Exception ex)
            {
                traceWriter.Error(ex.ToString());
                throw;
            }

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
            Collection<Binding> outputBindings, object input, IBinder binder, Dictionary<string, string> bindingData, 
            IDictionary<string, object> functionOutputs)
        {
            if (outputBindings == null || functionOutputs == null)
            {
                return;
            }

            foreach (Binding binding in outputBindings)
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

                Console.WriteLine(string.Format("Script function '{0}' changed. Reloading function.", _functionName));
            }
        }

        private Dictionary<string, object> CreateExecutionContext(object input, TraceWriter traceWriter, IBinder binder)
        {
            Type triggerParameterType = input.GetType();
            if (triggerParameterType == typeof(string) && IsJson((string)input))
            {
                // convert string into Dictionary (recursively) which Edge will convert into an object
                // before invoking the function
                input = JsonConvert.DeserializeObject<Dictionary<string, object>>((string)input, _dictionaryJsonConverter);
            }
            else if (triggerParameterType == typeof(HttpRequestMessage))
            {
                HttpRequestMessage request = (HttpRequestMessage)input;

                // convert the request to a json object
                // TODO: need to provide access to remaining request properties
                Dictionary<string, object> inputDictionary = new Dictionary<string, object>();
                inputDictionary["originalUrl"] = request.RequestUri.ToString();
                inputDictionary["method"] = request.Method.ToString().ToUpperInvariant();
                inputDictionary["query"] = request.GetQueryNameValuePairs().ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

                // if the content-type of the request is json, deserialize into an
                // object 
                string body = request.Content.ReadAsStringAsync().Result;
                if (request.Content.Headers.ContentType.MediaType == "application/json")
                {
                    input = JsonConvert.DeserializeObject<Dictionary<string, object>>(body, _dictionaryJsonConverter);
                }
                else
                {
                    input = body;
                }
                inputDictionary["body"] = input;

                input = inputDictionary;
            }

            // create a TraceWriter wrapper that can be exposed to Node.js
            var log = (Func<object, Task<object>>)((text) =>
            {
                traceWriter.Verbose((string)text);
                return Task.FromResult<object>(null);
            });

            string instanceId = Guid.NewGuid().ToString();
            var context = new Dictionary<string, object>()
            {
                { "instanceId", instanceId },
                { _triggerParameterName, input },
                { "log", log }
            };

            return context;
        }

        public static bool IsJson(string input)
        {
            input = input.Trim();
            return (input.StartsWith("{") && input.EndsWith("}"))
                   || (input.StartsWith("[") && input.EndsWith("]"));
        }
    }
}
