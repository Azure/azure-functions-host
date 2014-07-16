// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal class QueueTriggerExecutor : ITriggerExecutor<CloudQueueMessage>
    {
        private readonly ITriggeredFunctionInstanceFactory<CloudQueueMessage> _instanceFactory;
        private readonly IFunctionExecutor _innerExecutor;

        public QueueTriggerExecutor(ITriggeredFunctionInstanceFactory<CloudQueueMessage> instanceFactory,
            IFunctionExecutor innerExecutor)
        {
            _instanceFactory = instanceFactory;
            _innerExecutor = innerExecutor;
        }

        public bool Execute(CloudQueueMessage value)
        {
            Guid? parentId = QueueCausalityManager.GetOwner(value);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            IDelayedException exception = _innerExecutor.TryExecute(instance);
            return exception == null;
        }
    }
}
