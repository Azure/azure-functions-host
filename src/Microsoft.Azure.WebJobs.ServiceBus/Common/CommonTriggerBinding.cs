// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Generally usable binder. 
    internal class CommonTriggerBinding<TMessage, TTriggerValue> : ITriggerBinding
    {
        private readonly ITriggerBindingStrategy<TMessage, TTriggerValue> _hooks;
        private readonly ITriggerDataArgumentBinding<TTriggerValue> _argumentBinding;
        private readonly Func<ListenerFactoryContext, bool, Task<IListener>> _createListener;
        private readonly ParameterDescriptor _parameterDescriptor;
        private readonly bool _singleDispatch;

        public CommonTriggerBinding(
            ITriggerBindingStrategy<TMessage, TTriggerValue> hooks,
            ITriggerDataArgumentBinding<TTriggerValue> argumentBinding,
            Func<ListenerFactoryContext, bool, Task<IListener>> createListener,
            ParameterDescriptor parameterDescriptor,
            bool singleDispatch)
        {
            this._hooks = hooks;
            this._argumentBinding = argumentBinding;
            this._createListener = createListener;
            this._parameterDescriptor = parameterDescriptor;
            this._singleDispatch = singleDispatch;
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get
            {
                return _argumentBinding.BindingDataContract;
            }
        }

        public Type TriggerValueType
        {
            get
            {
                return typeof(TTriggerValue);
            }
        }

        public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
        {
            // If invoked from the dashboard, then 'value' is the string value passed in from the dashboard
            string invokeString = value as string;
            if (invokeString != null)
            {
                value = _hooks.ConvertFromString(invokeString);
            }

            TTriggerValue v2 = (TTriggerValue)value;
            return _argumentBinding.BindAsync(v2, context);
        }

        public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
        {
            var listenerTask = _createListener(context, _singleDispatch);
            return listenerTask;
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return _parameterDescriptor;
        }
    }
}