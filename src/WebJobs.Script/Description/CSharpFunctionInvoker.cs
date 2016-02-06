// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class CSharpFunctionInvoker : ScriptFunctionInvokerBase
    {
        private readonly ScriptHost _host;
        private readonly DictionaryJsonConverter _dictionaryJsonConverter = new DictionaryJsonConverter();
        private readonly TraceWriter _fileTraceWriter;
        private readonly FunctionMetadata _functionMetadata;
        private readonly BindingMetadata _trigger;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly string _triggerParameterName;
        private readonly bool _omitInputParameter;
        private MethodInfo _function;

        public CSharpFunctionInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata metadata, bool omitInputParameter, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            _host = host;
            _trigger = trigger;
            _triggerParameterName = trigger.Name;
            _omitInputParameter = omitInputParameter;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _functionMetadata = metadata;

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

        private MethodInfo Function
        {
            get
            {
                if (_function == null)
                {
                    // TODO
                    // Demand compile the function
                    // Filewatch for changes
                    _function = GetType().GetMethod("CSharpTest");
                }
                return _function;
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

                // if there are any binding parameters in the output bindings,
                // parse the input as json to get the binding data
                Dictionary<string, string> bindingData = new Dictionary<string, string>();
                if (_outputBindings.Any(p => p.HasBindingParameters) ||
                    _inputBindings.Any(p => p.HasBindingParameters))
                {
                    bindingData = GetBindingData(input);
                }
                bindingData["InvocationId"] = functionExecutionContext.InvocationId.ToString();

                Dictionary<string, object> executionContext = new Dictionary<string, object>();
                await ProcessInputBindingsAsync(binder, executionContext, bindingData);

                // TODO
                // - Based on the target method signature, map/convert parameters as necessary
                // - Handle Task return type properly, void methods, etc.
                // - Need to make the input binding data from above accessible to the function,
                //   mapping each input added to executionContext to its corresponding parameter
                //   based on name
                object[] converted = new object[2];
                converted[0] = parameters[0];
                converted[1] = parameters[1];
                Task<HttpResponseMessage> task = (Task<HttpResponseMessage>)Function.Invoke(null, converted);
                object functionResult = task.Result;

                // TODO: Need to get the binding outputs from the function (adding them to functionOutputs)
                // Need a programming model for this - perhaps if the function returns Dictionary<string, object>
                // which is the same as the Node.js model
                // Note: async functions can't return ref/out params.

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
                    BindingContext bindingContext = new BindingContext
                    {
                        Input = input,
                        Binder = binder,
                        BindingData = bindingData,
                        Value = value
                    };

                    await binding.BindAsync(bindingContext);
                }
            }
        }

        // TEMP
        // Example function that would be compiled from script
        public static Task<HttpResponseMessage> CSharpTest(HttpRequestMessage req, TraceWriter log)
        {
            var queryParamms = req.GetQueryNameValuePairs()
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

            log.Verbose(string.Format("CSharp HTTP trigger function processed a request. Name={0}", req.RequestUri));

            HttpResponseMessage res = null;
            string name;
            if (queryParamms.TryGetValue("name", out name))
            {
                res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("Hello " + name)
                };
            }
            else
            {
                res = new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("Please pass a name on the query string")
                };
            }

            return Task.FromResult(res);
        }
    }
}
