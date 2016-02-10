// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Bind EventData to itself 
    class SimpleTriggerArgumentBinding<TMessage, TTriggerValue> : ITriggerDataArgumentBinding<TTriggerValue>
    {
        protected ITriggerBindingStrategy<TMessage, TTriggerValue> _hooks;
        protected readonly IConverterManager _converterManager;

        // Caller can set it
        protected Dictionary<string, Type> _contract;
        protected internal Type _elementType;
        

        public SimpleTriggerArgumentBinding(ITriggerBindingStrategy<TMessage, TTriggerValue> hooks, IConverterManager converterManager)
        {
            _hooks = hooks;
            _contract = _hooks.GetStaticBindingContract();
            this._elementType = typeof(TMessage);
            _converterManager = converterManager;
        }

        IReadOnlyDictionary<string, Type> ITriggerDataArgumentBinding<TTriggerValue>.BindingDataContract
        {
            get
            {
                return _contract;
            }
        }

        public Type ValueType
        {
            get
            {
                return typeof(TTriggerValue);
            }
        }

        internal virtual object Convert(TMessage value, Dictionary<string, object> bindingData)
        {
            return value;
        }

        protected string ConvertToString(TMessage eventData)
        {
            var convert = _converterManager.GetConverter<TMessage, string>();
            var result = convert(eventData);
            return result;
        }

        public virtual Task<ITriggerData> BindAsync(TTriggerValue value, ValueBindingContext context)
        {
            Dictionary<string, object> bindingData = _hooks.GetContractInstance(value);

            TMessage eventData = _hooks.BindMessage(value, context);

            object userValue = this.Convert(eventData, bindingData);

            string invokeString = ConvertToString(eventData);

            IValueProvider valueProvider = new ConstantValueProvider(userValue, this._elementType, invokeString);
            var triggerData = new TriggerData(valueProvider, bindingData);

            return Task.FromResult<ITriggerData>(triggerData);
        }
    }
}