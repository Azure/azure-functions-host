// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal class ServiceBusTriggerExecutor : ITriggerExecutor<BrokeredMessage>
    {
        private readonly ITriggeredFunctionInstanceFactory<BrokeredMessage> _instanceFactory;
        private readonly IFunctionExecutor _innerExecutor;

        public ServiceBusTriggerExecutor(ITriggeredFunctionInstanceFactory<BrokeredMessage> instanceFactory,
            IFunctionExecutor innerExecutor)
        {
            _instanceFactory = instanceFactory;
            _innerExecutor = innerExecutor;
        }

        public async Task<bool> ExecuteAsync(BrokeredMessage value, CancellationToken cancellationToken)
        {
            Guid? parentId = ServiceBusCausalityHelper.GetOwner(value);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            IDelayedException exception = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);
            return exception == null;
        }
    }
}
