// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    // Bind TMessage --> String. Use IConverterManager for the conversion. 
    internal class StringTriggerArgumentBinding<TMessage, TTriggerValue> : SimpleTriggerArgumentBinding<TMessage, TTriggerValue>
    {
        public StringTriggerArgumentBinding(ITriggerBindingStrategy<TMessage, TTriggerValue> bindingStrategy, IConverterManager converterManager) : 
            base(bindingStrategy, converterManager)
        {
            this.ElementType = typeof(string);
        }

        internal override object Convert(TMessage value, Dictionary<string, object> bindingData)
        {
            var obj = this.ConvertToString(value);
            return obj;
        }
    }
}