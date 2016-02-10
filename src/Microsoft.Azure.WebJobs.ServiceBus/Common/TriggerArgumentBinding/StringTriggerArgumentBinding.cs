// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Bind TMessage --> String. USe IConverterManager for the conversion. 
    class StringTriggerArgumentBinding<TMessage, TTriggerValue> : SimpleTriggerArgumentBinding<TMessage, TTriggerValue>
    {
        public StringTriggerArgumentBinding(ITriggerBindingStrategy<TMessage, TTriggerValue> hooks, IConverterManager converterManager) : 
            base(hooks, converterManager)
        {
            this._elementType = typeof(string);
        }

        internal override object Convert(TMessage value, Dictionary<string, object> bindingData)
        {
            var obj = this.ConvertToString(value);
            return obj;
        }
    }
}