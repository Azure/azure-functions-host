// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Config
{
    public class ServiceBusConfigurationTests
    {
        [Fact]
        public void Constructor_SetsExpectedDefaults()
        {
            ServiceBusConfiguration config = new ServiceBusConfiguration();
            Assert.Equal(16, config.MessageOptions.MaxConcurrentCalls);
        }

        [Fact]
        public void ConnectionString_ReturnsExpectedDefaultUntilSetExplicitly()
        {
            ServiceBusConfiguration config = new ServiceBusConfiguration();

            string defaultConnection = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.ServiceBus);
            Assert.Equal(defaultConnection, config.ConnectionString);

            string testConnection = "testconnection";
            config.ConnectionString = testConnection;
            Assert.Equal(testConnection, config.ConnectionString);
        }

        [Fact]
        public void ConnectionString_SetterThrows_WhenMessagingProviderInitialized()
        {
            ServiceBusConfiguration config = new ServiceBusConfiguration();
            MessagingProvider provider = config.MessagingProvider;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                {
                    config.ConnectionString = "testconnection";
                });

            Assert.Equal("ConnectionString cannot be modified after the MessagingProvider has been initialized.", ex.Message);
        }
    }
}
