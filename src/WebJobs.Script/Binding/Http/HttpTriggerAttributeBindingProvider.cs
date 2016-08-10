// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding.Http
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

            // Can bind to user types, HttpRequestMessage and all the Read
            // Types supported by StreamValueBinder
            IEnumerable<Type> supportedTypes = StreamValueBinder.GetSupportedTypes(FileAccess.Read)
                .Union(new Type[] { typeof(HttpRequestMessage) });
            bool isSupportedTypeBinding = ValueBinder.MatchParameterType(parameter, supportedTypes);
            bool isUserTypeBinding = !isSupportedTypeBinding && IsValidUserType(parameter.ParameterType);
            if (!isSupportedTypeBinding && !isUserTypeBinding)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    "Can't bind HttpTriggerAttribute to type '{0}'.", parameter.ParameterType));
            }

            return Task.FromResult<ITriggerBinding>(new HttpTriggerBinding(context.Parameter, isUserTypeBinding));
        }

        public static bool IsValidUserType(Type type)
        {
            return !type.IsInterface && !type.IsPrimitive && !(type.Namespace == "System");
        }

        internal class HttpTriggerBinding : ITriggerBinding
        {
            private readonly ParameterInfo _parameter;
            private readonly IBindingDataProvider _bindingDataProvider;
            private readonly bool _isUserTypeBinding;

            public HttpTriggerBinding(ParameterInfo parameter, bool isUserTypeBinding)
            {
                _parameter = parameter;
                _isUserTypeBinding = isUserTypeBinding;

                if (_isUserTypeBinding)
                {
                    // Create the BindingDataProvider from the user Type. The BindingDataProvider
                    // is used to define the binding parameters that the binding exposes to other
                    // bindings (i.e. the properties of the POCO can be bound to by other bindings).
                    // It is also used to extract the binding data from an instance of the Type.
                    _bindingDataProvider = BindingDataProvider.FromType(parameter.ParameterType);
                }
            }

            public IReadOnlyDictionary<string, Type> BindingDataContract
            {
                get
                {
                    // if we're binding to a user Type, we'll have a contract,
                    // otherwise none
                    return _bindingDataProvider != null ? _bindingDataProvider.Contract : null;
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

                IValueProvider valueProvider = null;
                IReadOnlyDictionary<string, object> bindingData = null;
                string invokeString = request.ToString();
                if (_isUserTypeBinding)
                {
                    valueProvider = await CreateUserTypeValueProvider(request, invokeString);
                    if (_bindingDataProvider != null)
                    {
                        // binding data is defined by the user type
                        // the provider might be null if the Type is invalid, or if the Type
                        // has no public properties to bind to
                        bindingData = _bindingDataProvider.GetBindingData(valueProvider.GetValue());
                    }
                }
                else
                {
                    valueProvider = new HttpRequestValueBinder(_parameter, request, invokeString);
                    bindingData = await GetRequestBindingDataAsync(request);
                }

                return new TriggerData(valueProvider, bindingData);
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

            internal static async Task<IReadOnlyDictionary<string, object>> GetRequestBindingDataAsync(HttpRequestMessage request)
            {
                Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (request.Content != null && request.Content.Headers.ContentLength > 0)
                {
                    // pull binding data from the body
                    string body = await request.Content.ReadAsStringAsync();
                    Utility.ApplyBindingData(body, bindingData);
                }
                else
                {
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
                }

                return bindingData;
            }

            private async Task<IValueProvider> CreateUserTypeValueProvider(HttpRequestMessage request, string invokeString)
            {
                // First check to see if the WebHook data has already been deserialized,
                // otherwise read from the request content
                object value = null;
                if (!request.Properties.TryGetValue(ScriptConstants.AzureFunctionsWebHookDataKey, out value))
                {
                    if (request.Content != null && request.Content.Headers.ContentLength > 0)
                    {
                        // deserialize from message body
                        value = await request.Content.ReadAsAsync(_parameter.ParameterType);
                    }
                    else
                    {
                        // deserialize from Uri parameters
                        NameValueCollection parameters = request.RequestUri.ParseQueryString();
                        JObject intermediate = new JObject();
                        foreach (var propertyName in parameters.AllKeys)
                        {
                            intermediate[propertyName] = parameters[propertyName];
                        }
                        value = intermediate.ToObject(_parameter.ParameterType);
                    }
                }

                return new HttpUserTypeValueBinder(_parameter.ParameterType, value, invokeString);
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

                public override object GetValue()
                {
                    if (_parameter.ParameterType == typeof(HttpRequestMessage))
                    {
                        return _request;
                    }

                    return base.GetValue();
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

            /// <summary>
            /// ValueBinder for custom user Types
            /// </summary>
            private class HttpUserTypeValueBinder : IValueProvider
            {
                private readonly Type _type;
                private readonly object _value;
                private readonly string _invokeString;

                public HttpUserTypeValueBinder(Type type, object value, string invokeString)
                {
                    _type = type;
                    _value = value;
                    _invokeString = invokeString;
                }

                public Type Type
                {
                    get
                    {
                        return _type;
                    }
                }

                public object GetValue()
                {
                    return _value;
                }

                public string ToInvokeString()
                {
                    return _invokeString;
                }
            }

            private class NullListener : IListener
            {
                public void Cancel()
                {
                }

                public void Dispose()
                {
                }

                public Task StartAsync(CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }

                public Task StopAsync(CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }
            }
        }
    }
}
