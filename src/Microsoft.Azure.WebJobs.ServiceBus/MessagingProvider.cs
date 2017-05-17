// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Globalization;
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
        private readonly ServiceBusConfiguration _config;

        /// <summary>
        /// Cache of <see cref="MessagingFactory"/> instances by connection string.
        /// </summary>
        private readonly ConcurrentDictionary<string, MessagingFactory> _messagingFactoryCache = new ConcurrentDictionary<string, MessagingFactory>();

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
        /// Creates a <see cref="NamespaceManager"/>.
        /// </summary>
        /// <param name="connectionStringName">Optional connection string name indicating the connection string to use.
        /// If null, the default connection string on the <see cref="ServiceBusConfiguration"/> will be used.</param>
        /// <returns>The <see cref="NamespaceManager"/>.</returns>
        public virtual NamespaceManager CreateNamespaceManager(string connectionStringName = null)
        {
            string connectionString = GetConnectionString(connectionStringName);

            return NamespaceManager.CreateFromConnectionString(connectionString);
        }

        /// <summary>
        /// Creates a <see cref="MessagingFactory"/> for the specified ServiceBus entity.
        /// </summary>
        /// <param name="entityPath">The ServiceBus entity to create a <see cref="MessagingFactory"/> for.</param>
        /// <param name="connectionStringName">Optional connection string name indicating the connection string to use.
        /// If null, the default connection string on the <see cref="ServiceBusConfiguration"/> will be used.</param>
        /// <returns>A <see cref="MessagingFactory"/>.</returns>
        /// <remarks>For performance reasons, factories are cached per connection string.</remarks>
        public virtual MessagingFactory CreateMessagingFactory(string entityPath, string connectionStringName = null)
        {
            if (string.IsNullOrEmpty(entityPath))
            {
                throw new ArgumentNullException("entityPath");
            }

            string connectionString = GetConnectionString(connectionStringName);

            // We cache messaging factories per connection, in accordance with ServiceBus
            // performance guidelines.
            // https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-performance-improvements
            var messagingFactory = _messagingFactoryCache.GetOrAdd(connectionString, (c) =>
            {
                return MessagingFactory.CreateFromConnectionString(c);
            });

            return messagingFactory;
        }

        /// <summary>
        /// Creates a <see cref="MessageProcessor"/> for the specified ServiceBus entity.
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

        /// <summary>
        /// Creates a <see cref="MessageReceiver"/> for the specified ServiceBus entity.
        /// </summary>
        /// <remarks>
        /// You can override this method to customize the <see cref="MessageReceiver"/>.
        /// </remarks>
        /// <param name="factory">The <see cref="MessagingFactory"/> to use.</param>
        /// <param name="entityPath">The ServiceBus entity to create a <see cref="MessageReceiver"/> for.</param>
        /// <returns></returns>
        public virtual MessageReceiver CreateMessageReceiver(MessagingFactory factory, string entityPath)
        {
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }
            if (string.IsNullOrEmpty(entityPath))
            {
                throw new ArgumentNullException("entityPath");
            }

            MessageReceiver receiver = factory.CreateMessageReceiver(entityPath);
            receiver.PrefetchCount = _config.PrefetchCount;
            return receiver;
        }

        /// <summary>
        /// Gets the connection string for the specified connection string name.
        /// If no value is specified, the default connection string will be returned.
        /// </summary>
        /// <param name="connectionStringName">The connection string name.</param>
        /// <returns>The ServiceBus connection string.</returns>
        protected internal string GetConnectionString(string connectionStringName = null)
        {
            string connectionString = _config.ConnectionString;
            if (!string.IsNullOrEmpty(connectionStringName))
            {
                connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(connectionStringName);
            }

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Microsoft Azure WebJobs SDK ServiceBus connection string '{0}{1}' is missing or empty.",
                    "AzureWebJobs", connectionStringName ?? ConnectionStringNames.ServiceBus));
            }

            return connectionString;
        }
    }
}
