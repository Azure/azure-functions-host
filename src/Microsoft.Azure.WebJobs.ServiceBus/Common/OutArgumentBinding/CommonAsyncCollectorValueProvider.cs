// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    // TUser is the parameter type from the user function we're binding to. 
    // TMessage is from the underying IAsyncCollector<TMessage>
    internal class CommonAsyncCollectorValueProvider<TUser, TMessage> : IOrderedValueBinder
    {
        private readonly IFlushCollector<TMessage> _raw;
        private readonly TUser _object;
        private readonly string _invokeString;

        // raw is the underlying object (exposes a Flush method).
        // obj is athe front-end veneer to pass to the user function. 
        // calls to obj will trickle through adapters to be calls on raw. 
        public CommonAsyncCollectorValueProvider(TUser obj, IFlushCollector<TMessage> raw, string invokeString)
        {
            _raw = raw;
            _object = obj;
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
                return typeof(TUser);
            }
        }

        public object GetValue()
        {
            return _object;
        }

        public async Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            await _raw.FlushAsync();
        }

        public string ToInvokeString()
        {
            return _invokeString;
        }    
    }
}