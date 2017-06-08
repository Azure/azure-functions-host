// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: make this internal
    public class NodeLanguageInvoker : FunctionInvokerBase
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly ICompilationService<IJavaScriptCompilation> _compilationService;
        private readonly BindingMetadata _trigger;
        private readonly string _entryPoint;
        private readonly IMetricsLogger _metricsLogger;
        private Func<Task> _reloadScript;

        internal NodeLanguageInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings,
            ICompilationService<IJavaScriptCompilation> compilationService, ITraceWriterFactory traceWriterFactory = null)
            : base(host, functionMetadata, traceWriterFactory)
        {
            _trigger = trigger;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _entryPoint = functionMetadata.EntryPoint;
            _compilationService = compilationService;

            // If the compilation service writes to the common file system, we only want the primary host to generate
            // compilation output
            if (_compilationService.PersistsOutput)
            {
                _compilationService = new ConditionalJavaScriptCompilationService(Host.SettingsManager, compilationService, () => Host.IsPrimary);
            }

            _metricsLogger = Host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();

            _reloadScript = ReloadScriptAsync;
            _reloadScript = _reloadScript.Debounce();

            InitializeFileWatcherIfEnabled();
        }

        private async Task<IJavaScriptCompilation> CompileAndTraceAsync(LogTargets logTargets, bool throwOnCompilationError = true, bool suppressCompilationSummary = true)
        {
            try
            {
                IJavaScriptCompilation compilation = await _compilationService.GetFunctionCompilationAsync(Metadata);

                if (compilation.SupportsDiagnostics)
                {
                    ImmutableArray<Diagnostic> diagnostics = compilation.GetDiagnostics();
                    TraceCompilationDiagnostics(diagnostics, logTargets);

                    if (!suppressCompilationSummary)
                    {
                        bool compilationSucceeded = !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

                        TraceOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Compilation {0}.", compilationSucceeded ? "succeeded" : "failed"), TraceLevel.Info);
                    }
                }

                return compilation;
            }
            catch (CompilationErrorException ex)
            {
                TraceOnPrimaryHost("Function compilation error", TraceLevel.Error);
                TraceCompilationDiagnostics(ex.Diagnostics, logTargets);

                if (throwOnCompilationError)
                {
                    throw;
                }
            }

            return null;
        }

        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            // Ensure we're properly initialized
            // await _initializer.Value.ConfigureAwait(false);

            object input = parameters[0];
            string invocationId = context.ExecutionContext.InvocationId.ToString();
            DataType dataType = _trigger.DataType ?? DataType.String;

            var userTraceWriter = CreateUserTraceWriter(context.TraceWriter);
            var scriptExecutionContext = await CreateScriptExecutionContextAsync(input, dataType, userTraceWriter, context).ConfigureAwait(false);
            var bindingData = (Dictionary<string, object>)scriptExecutionContext["bindingData"];

            scriptExecutionContext["traceWriter"] = userTraceWriter;
            scriptExecutionContext["invocationId"] = context.ExecutionContext.InvocationId.ToString();
            await ProcessInputBindingsAsync(context.Binder, scriptExecutionContext, bindingData);

            // send message to Node RPC worker
            // TODO: move dispatcher.invoke to FunctionInvokerBase
            object functionResult = await Host.FunctionDispatcher.InvokeAsync(Metadata, scriptExecutionContext);

            await ProcessOutputBindingsAsync(_outputBindings, input, context.Binder, bindingData, scriptExecutionContext, functionResult);
        }

        private async Task ProcessInputBindingsAsync(Binder binder, Dictionary<string, object> executionContext, Dictionary<string, object> bindingData)
        {
            var bindings = (Dictionary<string, object>)executionContext["bindings"];
            var inputBindings = (Dictionary<string, KeyValuePair<object, DataType>>)executionContext["inputBindings"];

            // create an ordered array of all inputs and add to
            // the execution context. These will be promoted to
            // positional parameters
            var inputs = new List<object>
            {
                bindings[_trigger.Name]
            };
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

                // object converted;
                // if (TryConvertJson(bindingContext.Value, out converted))
                // {
                //    value = converted;
                // }

                bindings.Add(inputBinding.Metadata.Name, value);
                inputs.Add(value);
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

                // if (functionResult is IDictionary<string, object> functionOutputs)
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

        protected override void OnScriptFileChanged(FileSystemEventArgs e)
        {
            // The ScriptHost is already monitoring for changes to function.json, so we skip those
            string fileName = Path.GetFileName(e.Name);
            string fileExtension = Path.GetExtension(fileName);
            if (string.Compare(fileName, ScriptConstants.FunctionMetadataFileName, StringComparison.OrdinalIgnoreCase) != 0 &&
                _compilationService.SupportedFileTypes.Contains(fileExtension))
            {
                _reloadScript();
            }
        }

        private async Task ReloadScriptAsync()
        {
            // one of the script files for this function changed
            // force a reload on next execution
            // _functionLoader.Reset();
            // _scriptFunc = null;

            TraceOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Script for function '{0}' changed. Reloading.", Metadata.Name), TraceLevel.Info);

            IJavaScriptCompilation compilation = await CompileAndTraceAsync(LogTargets.User, throwOnCompilationError: false);

            if (compilation.SupportsDiagnostics)
            {
                bool compilationSucceeded = !compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
                TraceOnPrimaryHost(string.Format(CultureInfo.InvariantCulture, "Compilation {0}.", compilationSucceeded ? "succeeded" : "failed"), TraceLevel.Info);
            }
        }

        private async Task<Dictionary<string, object>> CreateScriptExecutionContextAsync(object input, DataType dataType, TraceWriter traceWriter, FunctionInvocationContext invocationContext)
        {
            var bindings = new Dictionary<string, object>();
            var inputBindings = new Dictionary<string, KeyValuePair<object, DataType>>();
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
                { "bindings", bindings },
                { "inputBindings", inputBindings },
            };

            if (!string.IsNullOrEmpty(_entryPoint))
            {
                context["_entryPoint"] = _entryPoint;
            }

            // convert the request to a json object
            if (input is HttpRequestMessage request)
            {
                var requestObject = await CreateRequestObjectAsync(request).ConfigureAwait(false);
                input = requestObject;

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

            // if the input is json, try converting to an object or array
            // TODO
            // object converted;
            // if (TryConvertJson(input, out converted))
            // {
                // input = converted;
            // }

            bindings.Add(_trigger.Name, input);
            inputBindings.Add(_trigger.Name, new KeyValuePair<object, DataType>(input, dataType));

            context.Add("_triggerType", _trigger.Type);

            return context;
        }

        internal static Dictionary<string, object> NormalizeBindingData(IDictionary<string, object> bindingData)
        {
            var normalizedBindingData = new Dictionary<string, object>();

            foreach (var pair in bindingData)
            {
                var value = pair.Value;
                if (value != null)
                {
                    var type = value.GetType();
                    if (value is IDictionary<string, object>)
                    {
                        value = NormalizeBindingData((IDictionary<string, object>)value);
                    }
                    else if (value is IDictionary<string, object>[])
                    {
                        value = ((IEnumerable<IDictionary<string, object>>)value)
                            .Select(p => NormalizeBindingData(p)).ToArray();
                    }
                    else if (value is IDictionary<string, string>)
                    {
                        value = ((IDictionary<string, string>)value)
                            .ToDictionary(p => Utility.ToLowerFirstCharacter(p.Key), p => p.Value);
                    }
                    else if (!IsEdgeSupportedType(type) && type.IsClass)
                    {
                        // for non primitive POCO types, we convert to
                        // a normalized dictionary
                        value = ToDictionary(value);
                    }
                }

                // "camel case" the normally Pascal cased properties by
                // converting the first letter to lower if needed
                // While for binding purposes case doesn't matter,
                // we want to normalize the case to something Node
                // users would expect to reference in code (e.g. "dequeueCount" not "DequeueCount")
                var name = pair.Key;
                name = Utility.ToLowerFirstCharacter(name);

                normalizedBindingData[name] = value;
            }

            return normalizedBindingData;
        }

        internal static IDictionary<string, object> ToDictionary(object value)
        {
            var properties = PropertyHelper.GetProperties(value);
            var dictionary = new Dictionary<string, object>();
            foreach (var property in properties)
            {
                if (IsEdgeSupportedType(property.Property.PropertyType))
                {
                    dictionary[Utility.ToLowerFirstCharacter(property.Name)] = property.GetValue(value);
                }
            }
            return dictionary;
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

        private static async Task<Dictionary<string, object>> CreateRequestObjectAsync(HttpRequestMessage request)
        {
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
            string rawBody = null;
            if (request.Content != null && request.Content.Headers.ContentLength > 0)
            {
                MediaTypeHeaderValue contentType = request.Content.Headers.ContentType;
                object jsonObject;
                object body = null;
                if (contentType != null)
                {
                    if (contentType.MediaType == "application/json")
                    {
                        body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (TryConvertJson((string)body, out jsonObject))
                        {
                            // if the content - type of the request is json, deserialize into an object or array
                            rawBody = (string)body;
                            body = jsonObject;
                        }
                    }
                    else if (contentType.MediaType == "application/octet-stream")
                    {
                        body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }

                if (body == null)
                {
                    // if we don't have a content type, default to reading as string
                    body = rawBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
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

            if (rawBody != null)
            {
                requestObject["rawBody"] = rawBody;
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
    }
}
