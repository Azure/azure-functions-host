// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal class ServiceBusTriggerExecutor
    {
        private readonly ITriggeredFunctionExecutor _innerExecutor;

        public ServiceBusTriggerExecutor(ITriggeredFunctionExecutor innerExecutor)
        {
            _innerExecutor = innerExecutor;
        }

        public async Task<FunctionResult> ExecuteAsync(BrokeredMessage value, CancellationToken cancellationToken)
        {
            Guid? parentId = ServiceBusCausalityHelper.GetOwner(value);
            TriggeredFunctionData input = new TriggeredFunctionData
            {
                ParentId = parentId,
                TriggerValue = value
            };
            return await _innerExecutor.TryExecuteAsync(input, cancellationToken);
        }
    }
}
