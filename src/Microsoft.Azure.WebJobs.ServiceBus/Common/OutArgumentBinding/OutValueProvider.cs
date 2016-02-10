// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // Bind to an 'Out T" parameter. 
    internal class OutValueProvider<TMessage> : IOrderedValueBinder
    {
        private readonly IFlushCollector<TMessage> _raw;
        private readonly string _invokeString;

        // raw is the underlying object (exposes a Flush method).
        // obj is athe front-end veneer to pass to the user function. 
        // calls to obj will trickle through adapters to be calls on raw. 
        public OutValueProvider(IFlushCollector<TMessage> raw, string invokeString)
        {
            _raw = raw;
            _invokeString = invokeString;
        }

        public BindStepOrder StepOrder
        {
            get { return BindStepOrder.Enqueue; }
        }

        public Type Type
        {
            get
            {
                return typeof(TMessage);
            }
        }

        public object GetValue()
        {
            // Out parameters are set on return
            return null;
        }

        public async Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            if (value == null)
            {
                // Nothing set
                return;
            }

            TMessage message = (TMessage)value;

            await _raw.AddAsync(message, cancellationToken);
            await _raw.FlushAsync();
        }

        public string ToInvokeString()
        {
            return _invokeString;
        }
    }
}