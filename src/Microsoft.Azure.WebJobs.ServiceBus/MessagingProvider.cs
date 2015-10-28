// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
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
        private readonly ServiceBusConfiguration _config;

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
        /// <remarks>
        /// This method is async because many of the interesting <see cref="MessagingFactory"/>
        /// create methods that overrides might want to call are async.
        /// </remarks>
        /// <returns>A Task that returns the <see cref="MessagingFactory"/>.</returns>
        public virtual Task<MessagingFactory> CreateMessagingFactoryAsync(string entityPath, string connectionStringName = null)
        {
            if (string.IsNullOrEmpty(entityPath))
            {
                throw new ArgumentNullException("entityPath");
            }

            string connectionString = GetConnectionString(connectionStringName);

            return Task.FromResult(MessagingFactory.CreateFromConnectionString(connectionString));
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
                    "AzureWebJobs", connectionStringName));
            }

            return connectionString;
        }
    }
}
