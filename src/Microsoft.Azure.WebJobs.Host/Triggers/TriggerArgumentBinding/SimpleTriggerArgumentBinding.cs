// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host
{
    // Bind EventData to itself 
    internal class SimpleTriggerArgumentBinding<TMessage, TTriggerValue> : ITriggerDataArgumentBinding<TTriggerValue>
    {
        private readonly ITriggerBindingStrategy<TMessage, TTriggerValue> _hooks;
        private readonly IConverterManager _converterManager;

        public SimpleTriggerArgumentBinding(ITriggerBindingStrategy<TMessage, TTriggerValue> hooks, IConverterManager converterManager)
        {
            this._hooks = hooks;
            this.Contract = Hooks.GetStaticBindingContract();
            this.ElementType = typeof(TMessage);
            _converterManager = converterManager;
        }

        // Caller can set it
        protected Dictionary<string, Type> Contract { get; set; }
        protected internal Type ElementType { get; set; }

        protected ITriggerBindingStrategy<TMessage, TTriggerValue> Hooks
        {
            get
            {
                return _hooks;
            }
        }

        IReadOnlyDictionary<string, Type> ITriggerDataArgumentBinding<TTriggerValue>.BindingDataContract
        {
            get
            {
                return Contract;
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
            var convert = _converterManager.GetConverter<TMessage, string, Attribute>();
            var result = convert(eventData, null, null);
            return result;
        }

        public virtual Task<ITriggerData> BindAsync(TTriggerValue value, ValueBindingContext context)
        {
            Dictionary<string, object> bindingData = Hooks.GetContractInstance(value);

            TMessage eventData = Hooks.BindSingle(value, context);

            object userValue = this.Convert(eventData, bindingData);

            string invokeString = ConvertToString(eventData);

            IValueProvider valueProvider = new ConstantValueProvider(userValue, this.ElementType, invokeString);
            var triggerData = new TriggerData(valueProvider, bindingData);

            return Task.FromResult<ITriggerData>(triggerData);
        }
    }
}