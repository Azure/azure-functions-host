// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Factory for creating <see cref="MessageProcessor"/> instances.
    /// </summary>
    public interface IMessageProcessorFactory
    {
        /// <summary>
        /// Creates a <see cref="MessageProcessor"/> using the specified context.
        /// </summary>
        /// <param name="context">The <see cref="MessageProcessorFactoryContext"/> to use.</param>
        /// <returns>A <see cref="MessageProcessor"/> instance.</returns>
        MessageProcessor Create(MessageProcessorFactoryContext context);
    }
}
