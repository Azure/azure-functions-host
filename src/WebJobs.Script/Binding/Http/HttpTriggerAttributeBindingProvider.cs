// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class HttpTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        public HttpTriggerAttributeBindingProvider()
        {
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            HttpTriggerAttribute attribute = parameter.GetCustomAttribute<HttpTriggerAttribute>(inherit: false);
            if (attribute == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            // Can bind to user types, HttpRequestMessage, object (for dynamic binding support) and all the Read
            // Types supported by StreamValueBinder
            IEnumerable<Type> supportedTypes = StreamValueBinder.GetSupportedTypes(FileAccess.Read)
                .Union(new Type[] { typeof(HttpResponseMessage), typeof(HttpRequestMessage), typeof(object) });
            bool isSupportedTypeBinding = ValueBinder.MatchParameterType(parameter, supportedTypes);
            bool isUserTypeBinding = !isSupportedTypeBinding && Utility.IsValidUserType(parameter.ParameterType);
            if (!isSupportedTypeBinding && !isUserTypeBinding)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    "Can't bind HttpTriggerAttribute to type '{0}'.", parameter.ParameterType));
            }

            return Task.FromResult<ITriggerBinding>(new HttpTriggerBinding(attribute, context.Parameter, isUserTypeBinding));
        }

        internal class HttpTriggerBinding : ITriggerBinding
        {
            private readonly ParameterInfo _parameter;
            private readonly IBindingDataProvider _bindingDataProvider;
            private readonly bool _isUserTypeBinding;
            private readonly bool _isProxy;
            private readonly Dictionary<string, Type> _bindingDataContract;
            private readonly HttpRouteFactory _httpRouteFactory = new HttpRouteFactory();

            public HttpTriggerBinding(HttpTriggerAttribute attribute, ParameterInfo parameter, bool isUserTypeBinding)
            {
                _parameter = parameter;
                _isUserTypeBinding = isUserTypeBinding;

                Dictionary<string, Type> aggregateDataContract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                if (_isUserTypeBinding)
                {
                    // Create the BindingDataProvider from the user Type. The BindingDataProvider
                    // is used to define the binding parameters that the binding exposes to other
                    // bindings (i.e. the properties of the POCO can be bound to by other bindings).
                    // It is also used to extract the binding data from an instance of the Type.
                    _bindingDataProvider = BindingDataProvider.FromType(parameter.ParameterType);
                    if (_bindingDataProvider.Contract != null)
                    {
                        aggregateDataContract.AddRange(_bindingDataProvider.Contract);
                    }
                }

                _isProxy = attribute.IsProxy;

                // add any route parameters to the contract
                if (!string.IsNullOrEmpty(attribute.RouteTemplate))
                {
                    var routeParameters = _httpRouteFactory.GetRouteParameters(attribute.RouteTemplate);
                    var parameters = ((MethodInfo)parameter.Member).GetParameters().ToDictionary(p => p.Name, p => p.ParameterType, StringComparer.OrdinalIgnoreCase);
                    foreach (string parameterName in routeParameters)
                    {
                        // don't override if the contract already includes a name
                        if (!aggregateDataContract.ContainsKey(parameterName))
                        {
                            // if there is a method parameter mapped to this parameter
                            // derive the Type from that
                            Type type;
                            if (!parameters.TryGetValue(parameterName, out type))
                            {
                                type = typeof(string);
                            }
                            aggregateDataContract[parameterName] = type;
                        }
                    }
                }

                _bindingDataContract = aggregateDataContract;
            }

            public IReadOnlyDictionary<string, Type> BindingDataContract
            {
                get
                {
                    return _bindingDataContract;
                }
            }

            public Type TriggerValueType
            {
                get { return typeof(HttpRequestMessage); }
            }

            public async Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
            {
                HttpRequestMessage request = value as HttpRequestMessage;
                if (request == null)
                {
                    throw new NotSupportedException("An HttpRequestMessage is required");
                }

                if (_isProxy)
                {
                    return await BindProxyAsync(request);
                }

                object poco = null;
                IReadOnlyDictionary<string, object> userTypeBindingData = null;
                IValueProvider valueProvider = null;
                string invokeString = ToInvokeString(request);
                if (_isUserTypeBinding)
                {
                    valueProvider = await CreateUserTypeValueProvider(request, invokeString);
                    if (_bindingDataProvider != null)
                    {
                        // some binding data is defined by the user type
                        // the provider might be null if the Type is invalid, or if the Type
                        // has no public properties to bind to
                        poco = await valueProvider.GetValueAsync();
                        userTypeBindingData = _bindingDataProvider.GetBindingData(poco);
                    }
                }
                else
                {
                    valueProvider = new HttpRequestValueBinder(_parameter, request, invokeString);
                }

                // create a modifiable collection of binding data and
                // copy in any initial binding data from the poco
                var aggregateBindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                aggregateBindingData.AddRange(userTypeBindingData);

                // Apply additional binding data coming from request route, query params, etc.
                var requestBindingData = await GetRequestBindingDataAsync(request, _bindingDataContract);
                aggregateBindingData.AddRange(requestBindingData);

                // apply binding data to the user type
                if (poco != null && aggregateBindingData.Count > 0)
                {
                    ApplyBindingData(poco, aggregateBindingData);
                }

                return new TriggerData(valueProvider, aggregateBindingData);
            }

            public static string ToInvokeString(HttpRequestMessage request)
            {
                // For display in the Dashboard, we want to be sure we don't log
                // any sensitive portions of the URI (e.g. query params, headers, etc.)
                string uri = request.RequestUri?.GetLeftPart(UriPartial.Path);

                return $"Method: {request.Method}, Uri: {uri}";
            }

            public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException("context");
                }

                return Task.FromResult<IListener>(new NullListener());
            }

            public ParameterDescriptor ToParameterDescriptor()
            {
                return new TriggerParameterDescriptor
                {
                    Name = _parameter.Name
                };
            }

            internal static void ApplyBindingData(object target, IDictionary<string, object> bindingData)
            {
                var propertyHelpers = PropertyHelper.GetProperties(target).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in bindingData)
                {
                    PropertyHelper propertyHelper;
                    if (propertyHelpers.TryGetValue(pair.Key, out propertyHelper) &&
                        propertyHelper.Property.CanWrite)
                    {
                        object value = pair.Value;
                        value = ConvertValueIfNecessary(value, propertyHelper.Property.PropertyType);
                        propertyHelper.SetValue(target, value);
                    }
                }
            }

            internal static async Task<IReadOnlyDictionary<string, object>> GetRequestBindingDataAsync(HttpRequestMessage request, Dictionary<string, Type> bindingDataContract = null)
            {
                Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (request.Content != null && request.Content.Headers.ContentLength > 0)
                {
                    // pull binding data from the body
                    string body = await request.Content.ReadAsStringAsync();
                    Utility.ApplyBindingData(body, bindingData);
                }

                // pull binding data from the query string
                var queryParameters = request.GetQueryNameValuePairs();
                foreach (var pair in queryParameters)
                {
                    if (string.Compare("code", pair.Key, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        // skip any system parameters that should not be bound to
                        continue;
                    }

                    bindingData[pair.Key] = pair.Value;
                }

                // apply any request route binding values
                object value = null;
                if (request.Properties.TryGetValue(ScriptConstants.AzureFunctionsHttpRouteDataKey, out value))
                {
                    Dictionary<string, object> routeBindingData = (Dictionary<string, object>)value;
                    foreach (var pair in routeBindingData)
                    {
                        // if we have a static binding contract that maps to this parameter
                        // derive the type from that contract mapping and perform any
                        // necessary conversion
                        value = pair.Value;
                        Type type = null;
                        if (bindingDataContract != null &&
                            bindingDataContract.TryGetValue(pair.Key, out type))
                        {
                            value = ConvertValueIfNecessary(value, type);
                        }

                        bindingData[pair.Key] = value;
                    }
                }

                return bindingData;
            }

            private async Task<IValueProvider> CreateUserTypeValueProvider(HttpRequestMessage request, string invokeString)
            {
                // First check to see if the WebHook data has already been deserialized,
                // otherwise read from the request body if present
                object value = null;
                if (!request.Properties.TryGetValue(ScriptConstants.AzureFunctionsWebHookDataKey, out value))
                {
                    if (request.Content != null && request.Content.Headers.ContentLength > 0)
                    {
                        // deserialize from message body
                        value = await request.Content.ReadAsAsync(_parameter.ParameterType);
                    }
                }

                if (value == null)
                {
                    // create an empty object
                    value = Activator.CreateInstance(_parameter.ParameterType);
                }

                return new SimpleValueProvider(_parameter.ParameterType, value, invokeString);
            }

            private async Task<ITriggerData> BindProxyAsync(HttpRequestMessage request)
            {
                IValueProvider valueProvider = null;
                string invokeString = ToInvokeString(request);

                HttpResponseMessage responseObjectInFunction = null;
                HttpRequestMessage requestObjectInFunction = null;

                string content = null;
                if (request.Content != null)
                {
                    content = await request.Content.ReadAsStringAsync();
                }

                if (_parameter.ParameterType == typeof(HttpResponseMessage))
                {
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (!ProxyHttpExtensions.TryDeserialize(content, out responseObjectInFunction))
                        {
                            throw new NotSupportedException("Invalid HttpResponseMessage object.");
                        }
                    }
                    else
                    {
                        responseObjectInFunction = new HttpResponseMessage();
                    }

                    valueProvider = new SimpleValueProvider(typeof(HttpResponseMessage), responseObjectInFunction, invokeString);
                }
                else if (_parameter.ParameterType == typeof(HttpRequestMessage))
                {
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (!ProxyHttpExtensions.TryDeserialize(content, out requestObjectInFunction))
                        {
                            throw new NotSupportedException("Invalid HttpRequestMessage object.");
                        }
                    }
                    else
                    {
                        requestObjectInFunction = new HttpRequestMessage();
                    }

                    // Adding the original request object to the newly created request object's properties as this will be needed when returning response to the client.
                    requestObjectInFunction.Properties[ScriptConstants.AzureFunctionsHttpProxyRoutingDataKey] = request;
                    request = requestObjectInFunction;

                    valueProvider = new SimpleValueProvider(typeof(HttpRequestMessage), requestObjectInFunction, invokeString);
                }

                return new TriggerData(valueProvider, null);
            }

            private static object ConvertValueIfNecessary(object value, Type targetType)
            {
                if (value != null && !targetType.IsAssignableFrom(value.GetType()))
                {
                    // if the type is nullable, we only need to convert to the
                    // correct underlying type
                    targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                    value = Convert.ChangeType(value, targetType);
                }

                return value;
            }

            /// <summary>
            /// ValueBinder for all our built in supported Types
            /// </summary>
            private class HttpRequestValueBinder : StreamValueBinder
            {
                private readonly ParameterInfo _parameter;
                private readonly HttpRequestMessage _request;
                private readonly string _invokeString;

                public HttpRequestValueBinder(ParameterInfo parameter, HttpRequestMessage request, string invokeString)
                    : base(parameter)
                {
                    _parameter = parameter;
                    _request = request;
                    _invokeString = invokeString;
                }

                public override async Task<object> GetValueAsync()
                {
                    if (_parameter.ParameterType == typeof(HttpRequestMessage))
                    {
                        return _request;
                    }
                    else if (_parameter.ParameterType == typeof(object))
                    {
                        // for dynamic, we read as an object, which will actually return
                        // a JObject which is dynamic
                        return await _request.Content.ReadAsAsync<object>();
                    }

                    return await base.GetValueAsync();
                }

                protected override Stream GetStream()
                {
                    Task<Stream> task = _request.Content.ReadAsStreamAsync();
                    task.Wait();
                    Stream stream = task.Result;

                    if (stream.Position > 0 && stream.CanSeek)
                    {
                        // we have to seek back to the beginning when reading as a stream,
                        // since once the Content is read somewhere else, the stream will
                        // be at the end
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    return stream;
                }

                public override string ToInvokeString()
                {
                    return _invokeString;
                }
            }            
        }
    }
}
