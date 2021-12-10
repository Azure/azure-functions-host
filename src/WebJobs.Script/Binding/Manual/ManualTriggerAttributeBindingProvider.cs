// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class ManualTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        public ManualTriggerAttributeBindingProvider()
        {
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ParameterInfo parameter = context.Parameter;
            ManualTriggerAttribute attribute = parameter.GetCustomAttribute<ManualTriggerAttribute>(inherit: false);
            if (attribute == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            // Can bind to user types, and all the Read Types supported by StreamValueBinder
            IEnumerable<Type> supportedTypes = StreamValueBinder.GetSupportedTypes(FileAccess.Read);
            bool isSupportedTypeBinding = ValueBinder.MatchParameterType(parameter, supportedTypes);
            bool isUserTypeBinding = !isSupportedTypeBinding && Utility.IsValidUserType(parameter.ParameterType);
            if (!isSupportedTypeBinding && !isUserTypeBinding)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    "Can't bind ManualTriggerAttribute to type '{0}'.", parameter.ParameterType));
            }

            return Task.FromResult<ITriggerBinding>(new ManualTriggerBinding(context.Parameter, isUserTypeBinding));
        }

        internal class ManualTriggerBinding : ITriggerBinding
        {
            private readonly ParameterInfo _parameter;
            private readonly IBindingDataProvider _bindingDataProvider;
            private readonly bool _isUserTypeBinding;

            public ManualTriggerBinding(ParameterInfo parameter, bool isUserTypeBinding)
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
                    return _bindingDataProvider?.Contract;
                }
            }

            public Type TriggerValueType
            {
                get { return _parameter.ParameterType; }
            }

            public async Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
            {
                IValueProvider valueProvider = null;
                IReadOnlyDictionary<string, object> bindingData = null;
                string invokeString = value?.ToString();
                if (_isUserTypeBinding)
                {
                    if (value != null && value.GetType() != _parameter.ParameterType)
                    {
                        if (value is string)
                        {
                            value = JsonConvert.DeserializeObject((string)value, _parameter.ParameterType);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unable to bind value to type {_parameter.ParameterType}");
                        }
                    }

                    valueProvider = new SimpleValueProvider(_parameter.ParameterType, value, invokeString);
                    if (_bindingDataProvider != null)
                    {
                        // binding data is defined by the user type
                        // the provider might be null if the Type is invalid, or if the Type
                        // has no public properties to bind to
                        bindingData = _bindingDataProvider.GetBindingData(await valueProvider.GetValueAsync());
                    }
                }
                else
                {
                    valueProvider = new SimpleValueProvider(_parameter.ParameterType, value, invokeString);
                    var bindingDataTemp = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    Utility.ApplyBindingData(value, bindingDataTemp);
                    bindingData = bindingDataTemp;
                }

                return new TriggerData(valueProvider, bindingData);
            }

            public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
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
        }
    }
}
