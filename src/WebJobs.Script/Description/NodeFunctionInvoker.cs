// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EdgeJs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    // TODO: make this internal
    public class NodeFunctionInvoker : IFunctionInvoker
    {
        private Func<object, Task<object>> _scriptFunc;
        private static string FunctionTemplate;
        private readonly Collection<Binding> _inputBindings;
        private readonly Collection<Binding> _outputBindings;
        private readonly string _triggerParameterName;
        private readonly string _script;

        static NodeFunctionInvoker()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream("Microsoft.Azure.WebJobs.Script.functionTemplate.js")))
            {
                FunctionTemplate = reader.ReadToEnd();
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

        internal NodeFunctionInvoker(string triggerParameterName, string scriptFilePath, Collection<Binding> inputBindings, Collection<Binding> outputBindings)
        {
            _triggerParameterName = triggerParameterName;
            scriptFilePath = scriptFilePath.Replace('\\', '/');
            _script = string.Format(FunctionTemplate, scriptFilePath);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
        }

        public async Task Invoke(object[] parameters)
        {
            object input = parameters[0];
            TraceWriter traceWriter = (TraceWriter)parameters[1];
            IBinder binder = (IBinder)parameters[2];

            var context = CreateContext(input, traceWriter, binder);

            // if there are any binding parameters in the output bindings,
            // parse the input as json to get the binding data
            Dictionary<string, string> bindingData = new Dictionary<string, string>();
            if (_outputBindings.Any(p => p.HasBindingParameters))
            {
                try
                {
                    // parse the object skipping any nested objects (binding data
                    // only includes top level properties)
                    JObject parsed = JObject.Parse(input as string);
                    bindingData = parsed.Children<JProperty>()
                        .Where(p => p.Value.Type != JTokenType.Object)
                        .ToDictionary(p => p.Name, p => (string)p);
                }
                catch
                {
                    // it's not an error if the incoming message isn't JSON
                    // there are cases where there will be output binding parameters
                    // that don't bind to JSON properties
                }
            }

            IDictionary<string, object> functionOutput = null;
            if (_outputBindings.Count > 0)
            {
                var output = (Func<object, Task<object>>)((binding) =>
                {
                    // cache the output value for the bind step below
                    functionOutput = binding as IDictionary<string, object>;
                    return Task.FromResult<object>(null);
                });
                context["output"] = output;
            }

            await ScriptFunc(context);

            // process output bindings
            if (functionOutput != null)
            {
                foreach (Binding binding in _outputBindings)
                {
                    // get the output value from the script
                    object value = null;
                    if (functionOutput.TryGetValue(binding.Name, out value))
                    {
                        byte[] bytes = null;
                        if (value.GetType() == typeof(string))
                        {
                            bytes = Encoding.UTF8.GetBytes((string)value);
                        }

                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            await binding.BindAsync(binder, ms, bindingData);
                        }
                    }
                }
            }
        }

        private Dictionary<string, object> CreateContext(object input, TraceWriter traceWriter, IBinder binder)
        {
            Type triggerParameterType = input.GetType();
            if (triggerParameterType == typeof(string))
            {
                // convert string into Dictionary (recursively) which Edge will convert into an object
                // before invoking the function
                input = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    (string)input, new DictionaryJsonConverter());
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
    }
}
