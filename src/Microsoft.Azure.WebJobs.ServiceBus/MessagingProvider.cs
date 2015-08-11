// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
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
        private readonly ServiceBusConfiguration _config;
        private NamespaceManager _namespaceManager;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="config">The <see cref="ServiceBusConfiguration"/>.</param>
        public MessagingProvider(ServiceBusConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            _config = config;
        }

        /// <summary>
        /// Gets the <see cref="NamespaceManager"/> to use.
        /// </summary>
        public virtual NamespaceManager NamespaceManager
        {
            get
            {
                if (_namespaceManager == null)
                {
                    _namespaceManager = NamespaceManager.CreateFromConnectionString(_config.ConnectionString);
                }
                return _namespaceManager;
            }
        }

        /// <summary>
        /// Creates a <see cref="MessagingFactory"/> for the specified ServiceBus entity.
        /// </summary>
        /// <param name="entityPath">The ServiceBus entity to create a <see cref="MessagingFactory"/> for.</param>
        /// <remarks>
        /// This method is async because many of the interesting <see cref="MessagingFactory"/>
        /// create methods that overrides might want to call are async.
        /// </remarks>
        /// <returns>A Task that returns the <see cref="MessagingFactory"/>.</returns>
        public virtual Task<MessagingFactory> CreateMessagingFactoryAsync(string entityPath)
        {
            if (string.IsNullOrEmpty(entityPath))
            {
                throw new ArgumentNullException("entityPath");
            }
            return Task.FromResult(MessagingFactory.CreateFromConnectionString(_config.ConnectionString));
        }

        /// <summary>
        /// Creates a <see cref="MessageProcessor"/> using the specified context.
        /// </summary>
        /// <param name="entityPath">The ServiceBus entity to create a <see cref="MessageProcessor"/> for.</param>
        /// <returns>The <see cref="MessageProcessor"/>.</returns>
        public virtual MessageProcessor CreateMessageProcessor(string entityPath)
        {
            if (string.IsNullOrEmpty(entityPath))
            {
                throw new ArgumentNullException("entityPath");
            }
            return new MessageProcessor(_config.MessageOptions);
        }
    }
}
