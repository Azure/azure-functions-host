// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
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

        public bool Execute(BrokeredMessage value)
        {
            Guid? parentId = ServiceBusCausalityHelper.GetOwner(value);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            IDelayedException exception = _innerExecutor.TryExecute(instance);
            return exception == null;
        }
    }
}
