// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    class ArrayTriggerArgumentBinding<TMessage, TTriggerValue> : SimpleTriggerArgumentBinding<TMessage, TTriggerValue>
    {
        private readonly SimpleTriggerArgumentBinding<TMessage, TTriggerValue> _innerBinding;

        public ArrayTriggerArgumentBinding(
            ITriggerBindingStrategy<TMessage, TTriggerValue> hooks,
            SimpleTriggerArgumentBinding<TMessage, TTriggerValue> innerBinding,
            IConverterManager converterManager) : base(hooks, converterManager)
        {
            this._innerBinding = innerBinding;
        }

        public override Task<ITriggerData> BindAsync(TTriggerValue value, ValueBindingContext context)
        {
            Dictionary<string, object> bindingData = _hooks.GetContractInstance(value);

            TMessage[] arrayRaw = _hooks.BindMessageArray(value, context);

            int len = arrayRaw.Length;
            Type elementType = _innerBinding._elementType;

            var arrayUser = Array.CreateInstance(elementType, len);
            for (int i = 0; i < len; i++)
            {
                TMessage item = arrayRaw[i];
                object obj = _innerBinding.Convert(item, null);
                arrayUser.SetValue(obj, i);
            }
            Type arrayType = elementType.MakeArrayType();

            IValueProvider valueProvider = new ConstantValueProvider(arrayUser, arrayType, "???");
            var triggerData = new TriggerData(valueProvider, bindingData);
            return Task.FromResult<ITriggerData>(triggerData);
        }
    }
}