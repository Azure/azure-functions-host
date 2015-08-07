// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal class DefaultMessageProcessorFactory : IMessageProcessorFactory
    {
        public MessageProcessor Create(MessageProcessorFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            return new MessageProcessor(context);
        }
    }
}
