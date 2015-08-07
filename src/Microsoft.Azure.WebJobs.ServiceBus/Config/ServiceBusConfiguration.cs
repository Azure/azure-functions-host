// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.ServiceBus.Listeners;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Configuration options for the ServiceBus extension.
    /// </summary>
    public class ServiceBusConfiguration
    {
        private bool _connectionStringSet;
        private string _connectionString;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public ServiceBusConfiguration()
        {
            MessageOptions = new OnMessageOptions
            {
                MaxConcurrentCalls = 16
            };
            MessageProcessorFactory = new DefaultMessageProcessorFactory();
        }

        /// <summary>
        /// Gets or sets the Azure ServiceBus connection string.
        /// </summary>
        public string ConnectionString
        {
            get
            {
                if (!_connectionStringSet)
                {
                    _connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
                    _connectionStringSet = true;
                }

                return _connectionString;
            }
            set
            {
                _connectionString = value;
                _connectionStringSet = true;
            }
        }

        /// <summary>
        /// Gets or sets the default <see cref="OnMessageOptions"/> that will be used by
        /// message receivers.
        /// </summary>
        public OnMessageOptions MessageOptions { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="IMessageProcessorFactory"/> that will be used to create
        /// <see cref="MessageProcessor"/> instances.
        /// </summary>
        public IMessageProcessorFactory MessageProcessorFactory
        {
            get;
            set;
        }
    }
}
