// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    // Bind a Trigger to a Poco type using JSon deserialization. Populate the binding contract with the properties from the Poco. 
    internal class PocoTriggerArgumentBinding<TMessage, TTriggerValue> : StringTriggerArgumentBinding<TMessage, TTriggerValue>
    {
        private IBindingDataProvider _provider;

        public PocoTriggerArgumentBinding(ITriggerBindingStrategy<TMessage, TTriggerValue> bindingStrategy, IConverterManager converterManager, Type elementType) : 
            base(bindingStrategy, converterManager)
        {
            this.ElementType = elementType;

            // Add properties ot binding data. Null if type doesn't expose it. 
            _provider = BindingDataProvider.FromType(elementType);
            if (_provider != null)
            {
                // Binding data from Poco properties takes precedence over builtins
                foreach (var kv in _provider.Contract)
                {
                    string name = kv.Key;
                    Type type = kv.Value;
                    Contract[name] = type;
                }
            }
        }

        internal override object Convert(TMessage value, Dictionary<string, object> bindingData)
        {
            string json = this.ConvertToString(value);

            object obj;
            try
            {
                obj = JsonConvert.DeserializeObject(json, this.ElementType);
            }
            catch (JsonException e)
            {
                // Easy to have the queue payload not deserialize properly. So give a useful error. 
                string msg = string.Format(CultureInfo.CurrentCulture,
@"Binding parameters to complex objects (such as '{0}') uses Json.NET serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The JSON parser failed: {1}
", this.ElementType.Name, e.Message);
                throw new InvalidOperationException(msg);
            }

            if (bindingData != null && _provider != null)
            {
                var pocoData = _provider.GetBindingData(obj);

                foreach (var kv in pocoData)
                {
                    string propName = kv.Key;
                    object propVal = kv.Value;
                    bindingData[propName] = propVal;
                }
            }

            return obj;
        }
    }
}