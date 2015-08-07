// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Provides context input for <see cref="IMessageProcessorFactory"/>
    /// </summary>
    public class MessageProcessorFactoryContext
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="messageOptions">The default <see cref="OnMessageOptions"/> to use.</param>
        /// <param name="entityPath">The path to the queue or topic.</param>
        /// <param name="trace">The <see cref="TraceWriter"/> to use.</param>
        public MessageProcessorFactoryContext(OnMessageOptions messageOptions, string entityPath, TraceWriter trace)
        {
            if (messageOptions == null)
            {
                throw new ArgumentNullException("messageOptions");
            }
            if (string.IsNullOrEmpty(entityPath))
            {
                throw new ArgumentNullException("entityPath");
            }
            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            MessageOptions = messageOptions;
            EntityPath = entityPath;
            Trace = trace;
        }

        /// <summary>
        /// Gets or sets the default <see cref="OnMessageOptions"/> that will be used by
        /// message receivers.
        /// </summary>
        public OnMessageOptions MessageOptions { get; set; }

        /// <summary>
        /// Gets the path to the ServiceBus queue or topic being processed.
        /// </summary>
        public string EntityPath { get; private set; }

        /// <summary>
        /// Gets the <see cref="TraceWriter"/>.
        /// </summary>
        public TraceWriter Trace { get; private set; }
    }
}
