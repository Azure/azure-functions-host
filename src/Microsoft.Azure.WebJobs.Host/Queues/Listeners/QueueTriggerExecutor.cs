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
        private readonly ITriggeredFunctionExecutor _innerExecutor;

        public QueueTriggerExecutor(ITriggeredFunctionExecutor innerExecutor)
        {
            _innerExecutor = innerExecutor;
        }

        public async Task<FunctionResult> ExecuteAsync(IStorageQueueMessage value, CancellationToken cancellationToken)
        {
            Guid? parentId = QueueCausalityManager.GetOwner(value);
            TriggeredFunctionData input = new TriggeredFunctionData
            {
                ParentId = parentId,
                TriggerValue = value
            };
            return await _innerExecutor.TryExecuteAsync(input, cancellationToken);
        }
    }
}
