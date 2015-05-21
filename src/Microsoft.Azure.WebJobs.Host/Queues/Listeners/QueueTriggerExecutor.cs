// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class QueueTriggerExecutor : ITriggerExecutor<IStorageQueueMessage>
    {
        private readonly ITriggeredFunctionExecutor<IStorageQueueMessage> _innerExecutor;

        public QueueTriggerExecutor(ITriggeredFunctionExecutor<IStorageQueueMessage> innerExecutor)
        {
            _innerExecutor = innerExecutor;
        }

        public async Task<bool> ExecuteAsync(IStorageQueueMessage value, CancellationToken cancellationToken)
        {
            Guid? parentId = QueueCausalityManager.GetOwner(value);
            TriggeredFunctionData<IStorageQueueMessage> input = new TriggeredFunctionData<IStorageQueueMessage>
            {
                ParentId = parentId,
                TriggerValue = value
            };
            return await _innerExecutor.TryExecuteAsync(input, cancellationToken);
        }
    }
}
