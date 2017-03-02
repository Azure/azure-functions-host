// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class NodeLanguageInvoker : LanguageFunctionInvokerBase
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private static readonly object _initializationSyncRoot = new object();
        private readonly BindingMetadata _trigger;
        private readonly string _entryPoint;
        private string scriptFilePath;
        private Func<object, Task<object>> _scriptFunc;
        private static bool _initialized = false;

        internal NodeLanguageInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, ITraceWriterFactory traceWriterFactory = null)
            : base(host, functionMetadata, traceWriterFactory)
        {
            this.GetRpcClient().SetupNodeRpcWorker(this.TraceWriter);
            _trigger = trigger;
            scriptFilePath = functionMetadata.ScriptFile.Replace('\\', '/');
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _entryPoint = functionMetadata.EntryPoint;

            InitializeFileWatcherIfEnabled();
        }

        // TODO dup form script function invoker
        protected static object ConvertInput(object input)
        {
            if (input != null)
            {
                // perform any required input conversions
                HttpRequestMessage request = input as HttpRequestMessage;
                if (request != null)
                {
                    // TODO: Handle other content types? (E.g. byte[])
                    if (request.Content != null && request.Content.Headers.ContentLength > 0)
                    {
                        return ((HttpRequestMessage)input).Content.ReadAsStringAsync().Result;
                    }
                }
            }

            return input;
        }

        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            EnsureInitialized();
            object input = parameters[0];
            string invocationId = context.ExecutionContext.InvocationId.ToString();
            DataType dataType = _trigger.DataType ?? DataType.String;
            var userTraceWriter = CreateUserTraceWriter(context.TraceWriter);
            object convertedInput = ConvertInput(input);
            Utility.ApplyBindingData(convertedInput, context.Binder.BindingData);

            Dictionary<string, object> scriptExecutionContext = CreateScriptExecutionContext(input, dataType, userTraceWriter, context);
            var bindingData = (Dictionary<string, object>)scriptExecutionContext["bindingData"];
            scriptExecutionContext["traceWriter"] = userTraceWriter;
            scriptExecutionContext["systemTraceWriter"] = this.TraceWriter;
            await ProcessInputBindingsAsync(context.Binder, scriptExecutionContext, bindingData);

            // send message to Node RPC worker
            // TODO: move dispatcher.invoke to FunctionInvokerBase
            object functionResult = await Host.FunctionDispatcher.Invoke(Metadata, parameters);

            await ProcessOutputBindingsAsync(_outputBindings, input, context.Binder, bindingData, scriptExecutionContext, functionResult);
        }

        public static dynamic ToDynamic(object value)
        {
            IDictionary<string, object> expando = new ExpandoObject();

            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value.GetType()))
            {
                expando.Add(property.Name, property.GetValue(value));
            }

            return expando as ExpandoObject;
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                lock (_initializationSyncRoot)
                {
                    if (!_initialized)
                    {
                        Initialize();
                        _initialized = true;
                    }
                }
            }
        }

        private async Task ProcessInputBindingsAsync(Binder binder, Dictionary<string, object> executionContext, Dictionary<string, object> bindingData)
        {
            var bindings = (Dictionary<string, object>)executionContext["bindings"];

            var inputsWithDataTypes = (Dictionary<object, string>)executionContext["inputsWithDataTypes"];
            var inputBindings = (Dictionary<string, KeyValuePair<object, DataType>>)executionContext["inputBindings"];

            // create an ordered array of all inputs and add to
            // the execution context. These will be promoted to
            // positional parameters
            List<object> inputs = new List<object>();
            inputs.Add(bindings[_trigger.Name]);

            var nonTriggerInputBindings = _inputBindings.Where(p => !p.Metadata.IsTrigger);
            foreach (var inputBinding in nonTriggerInputBindings)
            {
                BindingContext bindingContext = new BindingContext
                {
                    Binder = binder,
                    BindingData = bindingData,
                    DataType = inputBinding.Metadata.DataType ?? DataType.String,
                    Cardinality = inputBinding.Metadata.Cardinality ?? Cardinality.One
                };
                await inputBinding.BindAsync(bindingContext);

                // Perform any JSON to object conversions if the
                // value is JSON or a JToken
                object value = bindingContext.Value;

                bindings.Add(inputBinding.Metadata.Name, value);
                inputs.Add(value);
                inputsWithDataTypes.Add(value, bindingContext.DataType.ToString());
                inputBindings.Add(inputBinding.Metadata.Name, new KeyValuePair<object, DataType>(value, bindingContext.DataType));
            }

            executionContext["_inputs"] = inputs;
        }

        private static async Task ProcessOutputBindingsAsync(Collection<FunctionBinding> outputBindings, object input, Binder binder,
            Dictionary<string, object> bindingData, Dictionary<string, object> scriptExecutionContext, object functionResult)
        {
            if (outputBindings == null)
            {
                return;
            }

            var bindings = (Dictionary<string, object>)scriptExecutionContext["bindings"];
            var returnValueBinding = outputBindings.SingleOrDefault(p => p.Metadata.IsReturn);
            if (returnValueBinding != null)
            {
                // if there is a $return binding, bind the entire function return to it
                // if additional output bindings need to be bound, they'll have to use the explicit
                // context.bindings mechanism to set values, not return value.
                bindings[ScriptConstants.SystemReturnParameterBindingName] = functionResult;
            }
            else
            {
                // if the function returned binding values via the function result,
                // apply them to context.bindings

                // TODO convert string response to expando
                string stringContent = functionResult as string;
                if (stringContent != null)
                {
                    try
                    {
                        // attempt to read the content as JObject/JArray
                        functionResult = JsonConvert.DeserializeObject(stringContent);
                    }
                    catch (JsonException)
                    {
                        // not a json response
                    }
                }

                // see if the content is a response object, defining http response properties
                IDictionary<string, object> functionOutputs = null;
                if (functionResult is JObject)
                {
                    functionOutputs = JsonConvert.DeserializeObject<ExpandoObject>(stringContent);
                }
                if (functionOutputs != null)
                {
                    foreach (var output in functionOutputs)
                    {
                        bindings[output.Key] = output.Value;
                    }
                }
            }

            foreach (FunctionBinding binding in outputBindings)
            {
                // get the output value from the script
                object value = null;
                bool haveValue = bindings.TryGetValue(binding.Metadata.Name, out value);
                if (!haveValue && string.Compare(binding.Metadata.Type, "http", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // http bindings support a special context.req/context.res programming
                    // model, so we must map that back to the actual binding name if a value
                    // wasn't provided using the binding name itself
                    haveValue = bindings.TryGetValue("res", out value);
                }

                // apply the value to the binding
                if (haveValue && value != null)
                {
                    BindingContext bindingContext = new BindingContext
                    {
                        TriggerValue = input,
                        Binder = binder,
                        BindingData = bindingData,
                        Value = value
                    };
                    await binding.BindAsync(bindingContext);
                }
            }
        }

        // internal void OnHostRestart()
        // {
            // TODO
            // ClearRequireCacheFunc(null).GetAwaiter().GetResult();
            // this.rpc.ClearRequiredCache();
        // }

        protected override void OnScriptFileChanged(FileSystemEventArgs e)
        {
            if (_scriptFunc == null)
            {
                // we're not loaded yet, so nothing to reload
                return;
            }

            // clear the node module cache
            // This is done for any files to ensure that, if a file change triggers
            // a host restart, we leave the cache clean.
            // TODO
            // ClearRequireCacheFunc(null).GetAwaiter().GetResult();

            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileName = Path.GetFileName(e.Name);
            if (string.Compare(fileName, ScriptConstants.FunctionMetadataFileName, StringComparison.OrdinalIgnoreCase) != 0)
            {
                // one of the script files for this function changed
                // force a reload on next execution
                _scriptFunc = null;

                TraceOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Script for function '{0}' changed. Reloading.", Metadata.Name), System.Diagnostics.TraceLevel.Info);
            }
        }

        private Dictionary<string, object> CreateScriptExecutionContext(object input, DataType dataType, TraceWriter traceWriter, FunctionInvocationContext invocationContext)
        {
            // create a TraceWriter wrapper that can be exposed to Node.js
            var log = (Func<object, Task<object>>)(p =>
            {
                var logData = (IDictionary<string, object>)p;
                string message = (string)logData["msg"];
                if (message != null)
                {
                    try
                    {
                        TraceLevel level = (TraceLevel)logData["lvl"];
                        var evt = new TraceEvent(level, message);
                        traceWriter.Trace(evt);
                    }
                    catch (ObjectDisposedException)
                    {
                        // if a function attempts to write to a disposed
                        // TraceWriter. Might happen if a function tries to
                        // log after calling done()
                    }
                }

                return Task.FromResult<object>(null);
            });

            var bindings = new Dictionary<string, object>();
            var inputsWithDataTypes = new Dictionary<object, string>();
            var inputBindings = new Dictionary<string, KeyValuePair<object, DataType>>();
            var bind = (Func<object, Task<object>>)(p =>
            {
                IDictionary<string, object> bindValues = (IDictionary<string, object>)p;
                foreach (var bindValue in bindValues)
                {
                    bindings[bindValue.Key] = bindValue.Value;
                }
                return Task.FromResult<object>(null);
            });

            var executionContext = new Dictionary<string, object>
            {
                ["invocationId"] = invocationContext.ExecutionContext.InvocationId,
                ["functionName"] = invocationContext.ExecutionContext.FunctionName,
                ["functionDirectory"] = invocationContext.ExecutionContext.FunctionDirectory,
            };

            var context = new Dictionary<string, object>()
            {
                { "invocationId", invocationContext.ExecutionContext.InvocationId },
                { "executionContext", executionContext },
                { "log", log },
                { "bindings", bindings },
                { "inputsWithDataTypes", inputsWithDataTypes },
                { "inputBindings", inputBindings },
                { "bind", bind }
            };

            if (!string.IsNullOrEmpty(_entryPoint))
            {
                context["_entryPoint"] = _entryPoint;
            }

            if (input is HttpRequestMessage)
            {
                // convert the request to a json object
                HttpRequestMessage request = (HttpRequestMessage)input;
                string rawBody = null;
                var requestObject = CreateRequestObject(request, out rawBody);
                input = requestObject;

                if (rawBody != null)
                {
                    requestObject["rawBody"] = rawBody;
                }

                // If this is a WebHook function, the input should be the
                // request body
                var httpTrigger = _inputBindings.OfType<ExtensionBinding>().SingleOrDefault(p => p.Metadata.IsTrigger)?
                    .Attributes.OfType<HttpTriggerAttribute>().SingleOrDefault();
                if (httpTrigger != null && !string.IsNullOrEmpty(httpTrigger.WebHookType))
                {
                    requestObject.TryGetValue("body", out input);

                    // TODO

                    inputBindings.Add("webhookReq", new KeyValuePair<object, DataType>(requestObject, dataType));
                }

                // make the entire request object available as well
                // this is symmetric with context.res which we also support
                context["req"] = requestObject;
            }
            else if (input is TimerInfo)
            {
                // TODO: Need to generalize this model rather than hardcode
                // so other extensions can also express their Node.js object model
                TimerInfo timerInfo = (TimerInfo)input;
                var inputValues = new Dictionary<string, object>()
                {
                    { "isPastDue", timerInfo.IsPastDue }
                };
                if (timerInfo.ScheduleStatus != null)
                {
                    inputValues["last"] = timerInfo.ScheduleStatus.Last.ToString("s", CultureInfo.InvariantCulture);
                    inputValues["next"] = timerInfo.ScheduleStatus.Next.ToString("s", CultureInfo.InvariantCulture);
                }
                input = inputValues;
            }
            else if (input is Stream)
            {
                FunctionBinding.ConvertStreamToValue((Stream)input, dataType, ref input);
            }

            Utility.ApplyBindingData(input, invocationContext.Binder.BindingData);
            var bindingData = NormalizeBindingData(invocationContext.Binder.BindingData);
            bindingData["invocationId"] = invocationContext.ExecutionContext.InvocationId.ToString();
            context["bindingData"] = bindingData;
            bindings.Add(_trigger.Name, input);
            if (input != null)
            {
                inputsWithDataTypes.Add(input, dataType.ToString());
                inputBindings.Add(_trigger.Name, new KeyValuePair<object, DataType>(input, dataType));
            }

            context.Add("_triggerType", _trigger.Type);

            return context;
        }

        private static Dictionary<string, object> NormalizeBindingData(Dictionary<string, object> bindingData)
        {
            Dictionary<string, object> normalizedBindingData = new Dictionary<string, object>();

            foreach (var pair in bindingData)
            {
                var name = pair.Key;
                var value = pair.Value;
                if (value != null && !IsEdgeSupportedType(value.GetType()))
                {
                    // we must convert values to types supported by Edge
                    // marshalling as needed
                    value = value.ToString();
                }

                // "camel case" the normally Pascal cased properties by
                // converting the first letter to lower if needed
                // While for binding purposes case doesn't matter,
                // we want to normalize the case to something Node
                // users would expect to reference in code (e.g. "dequeueCount" not "DequeueCount")
                name = Utility.ToLowerFirstCharacter(name);

                normalizedBindingData[name] = value;
            }

            return normalizedBindingData;
        }

        internal static bool IsEdgeSupportedType(Type type)
        {
            if (type == typeof(int) ||
                type == typeof(double) ||
                type == typeof(string) ||
                type == typeof(bool) ||
                type == typeof(byte[]) ||
                type == typeof(object[]))
            {
                return true;
            }

            return false;
        }

        private static Dictionary<string, object> CreateRequestObject(HttpRequestMessage request, out string rawBody)
        {
            rawBody = null;

            // TODO: need to provide access to remaining request properties
            Dictionary<string, object> requestObject = new Dictionary<string, object>();
            requestObject["originalUrl"] = request.RequestUri.ToString();
            requestObject["method"] = request.Method.ToString().ToUpperInvariant();
            requestObject["query"] = request.GetQueryParameterDictionary();

            // since HTTP headers are case insensitive, we lower-case the keys
            // as does Node.js request object
            var headers = request.GetRawHeaders().ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value);
            requestObject["headers"] = headers;

            // if the request includes a body, add it to the request object
            if (request.Content != null && request.Content.Headers.ContentLength > 0)
            {
                MediaTypeHeaderValue contentType = request.Content.Headers.ContentType;
                object jsonObject;
                object body = null;
                if (contentType != null)
                {
                    if (contentType.MediaType == "application/json")
                    {
                        body = request.Content.ReadAsStringAsync().Result;
                        if (TryConvertJson((string)body, out jsonObject))
                        {
                            // if the content - type of the request is json, deserialize into an object or array
                            rawBody = (string)body;
                            body = jsonObject;
                        }
                    }
                    else if (contentType.MediaType == "application/octet-stream")
                    {
                        body = request.Content.ReadAsByteArrayAsync().Result;
                    }
                }

                if (body == null)
                {
                    // if we don't have a content type, default to reading as string
                    body = rawBody = request.Content.ReadAsStringAsync().Result;
                }

                requestObject["body"] = body;
            }

            // Apply any captured route parameters to the params collection
            object value = null;
            if (request.Properties.TryGetValue(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, out value))
            {
                Dictionary<string, object> routeData = (Dictionary<string, object>)value;
                requestObject["params"] = routeData;
            }

            return requestObject;
        }

        /// <summary>
        /// If the specified input is a JSON string, an array of JSON strings, or JToken, attempt to deserialize it into
        /// an object or array.
        /// </summary>
        internal static bool TryConvertJson(object input, out object result)
        {
            if (input is JToken)
            {
                input = input.ToString();
            }

            result = null;
            string inputString = input as string;
            string[] inputStrings = input as string[];
            if (inputString == null && inputStrings == null)
            {
                return false;
            }

            if (Utility.IsJson(inputString))
            {
                // if the input is json, try converting to an object or array
                if (TryDeserializeJsonObjectOrArray(inputString, out result))
                {
                    return true;
                }
            }
            else if (inputStrings != null && inputStrings.All(p => Utility.IsJson(p)))
            {
                // if the input is an array of json strings, try converting to
                // an array
                object[] results = new object[inputStrings.Length];
                for (int i = 0; i < inputStrings.Length; i++)
                {
                    if (TryDeserializeJsonObjectOrArray(inputStrings[i], out result))
                    {
                        results[i] = result;
                    }
                    else
                    {
                        return false;
                    }
                }
                result = results;
                return true;
            }

            return false;
        }

        private static bool TryDeserializeJsonObjectOrArray(string json, out object result)
        {
            result = null;

            // if the input is json, try converting to an object or array
            ExpandoObject obj;
            ExpandoObject[] objArray;
            if (TryDeserializeJson(json, out obj))
            {
                result = obj;
                return true;
            }
            else if (TryDeserializeJson(json, out objArray))
            {
                result = objArray;
                return true;
            }

            return false;
        }

        private static bool TryDeserializeJson<TResult>(string json, out TResult result)
        {
            result = default(TResult);

            try
            {
                result = JsonConvert.DeserializeObject<TResult>(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Performs required static initialization in the Edge context.
        /// </summary>
        private static void Initialize()
        {
            // TODO
            // var handle = (Func<object, Task<object>>)(err =>
            // {
            //    if (UnhandledException != null)
            //    {
            //        // raise the event to allow subscribers to handle
            //        var ex = new InvalidOperationException((string)err);
            //        UnhandledException(null, new UnhandledExceptionEventArgs(ex, true));

            // Ensure that we allow the unhandled exception to kill the process.
            //         unhandled Node global exceptions should never be swallowed.
            //        throw ex;
            //    }
            //    return Task.FromResult<object>(null);
            // });
            // var context = new Dictionary<string, object>()
            // {
            //    { "handleUncaughtException", handle }
            // };
            // TODO
            // GlobalInitializationFunc(context).GetAwaiter().GetResult();
        }
    }
}
