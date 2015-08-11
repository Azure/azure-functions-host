// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// This class provides factory methods for the creation of instances
    /// used for ServiceBus message processing.
    /// </summary>
    public class MessagingProvider
    {
        private readonly string _connectionString;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="connectionString">The ServiceBus connection string.</param>
        public MessagingProvider(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException("connectionString");
            }
            _connectionString = connectionString;
        }

        /// <summary>
        /// Creates a <see cref="MessagingFactory"/> for the specified ServiceBus entity.
        /// </summary>
        /// <param name="entity">The ServiceBus entity to create a <see cref="MessagingFactory"/> for.</param>
        /// <remarks>
        /// This method is async because many of the interesting <see cref="MessagingFactory"/>
        /// create methods that overrides might want to call are async.
        /// </remarks>
        /// <returns>A Task that returns the <see cref="MessagingFactory"/>.</returns>
        public virtual Task<MessagingFactory> CreateMessagingFactoryAsync(string entity)
        {
            if (string.IsNullOrEmpty(entity))
            {
                throw new ArgumentNullException("entity");
            }
            return Task.FromResult(MessagingFactory.CreateFromConnectionString(_connectionString));
        }

        /// <summary>
        /// Creates a <see cref="NamespaceManager"/> for the specified ServiceBus entity.
        /// </summary>
        /// <param name="entity">The ServiceBus entity to create a <see cref="NamespaceManager"/> for.</param>
        /// <returns>The <see cref="NamespaceManager"/>.</returns>
        public virtual NamespaceManager CreateNamespaceManager(string entity)
        {
            if (string.IsNullOrEmpty(entity))
            {
                throw new ArgumentNullException("entity");
            }
            return NamespaceManager.CreateFromConnectionString(_connectionString);
        }

        /// <summary>
        /// Creates a <see cref="MessageProcessor"/> using the specified context.
        /// </summary>
        /// <param name="entity">The ServiceBus entity.</param>
        /// <param name="messageOptions">The <see cref="OnMessageOptions"/> to use.</param>
        /// <param name="trace">The <see cref="TraceWriter"/> to use.</param>
        /// <returns>The <see cref="MessageProcessor"/>.</returns>
        public virtual MessageProcessor CreateMessageProcessor(string entity, OnMessageOptions messageOptions, TraceWriter trace)
        {
            if (string.IsNullOrEmpty(entity))
            {
                throw new ArgumentNullException("entity");
            }
            if (messageOptions == null)
            {
                throw new ArgumentNullException("messageOptions");
            }
            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }
            return new MessageProcessor(messageOptions);
        }
    }
}
