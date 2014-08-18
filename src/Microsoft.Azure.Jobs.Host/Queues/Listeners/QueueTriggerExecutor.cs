// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
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

        public async Task<bool> ExecuteAsync(CloudQueueMessage value, CancellationToken cancellationToken)
        {
            Guid? parentId = QueueCausalityManager.GetOwner(value);
            IFunctionInstance instance = _instanceFactory.Create(value, parentId);
            IDelayedException exception = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);
            return exception == null;
        }
    }
}
