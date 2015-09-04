// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class MessagingProviderTests : IDisposable
    {
        private readonly ServiceBusConfiguration _config;
        private readonly MessagingProvider _provider;

        public MessagingProviderTests()
        {
            string defaultConnection = "Endpoint=sb://default.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=";
            _config = new ServiceBusConfiguration
            {
                ConnectionString = defaultConnection
            };
            _provider = new MessagingProvider(_config);

            string overrideConnection = "Endpoint=sb://override.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=";
            Environment.SetEnvironmentVariable("AzureWebJobsServiceBusOverride", overrideConnection);
        }

        [Fact]
        public async Task CreateMessagingFactoryAsync_ReturnsExpectedFactory()
        {
            // default connection
            MessagingFactory factory = await _provider.CreateMessagingFactoryAsync("test");
            Assert.Equal("default.servicebus.windows.net", factory.Address.Host);

            // override connection
            factory = await _provider.CreateMessagingFactoryAsync("test", "ServiceBusOverride");
            Assert.Equal("override.servicebus.windows.net", factory.Address.Host);
        }

        [Fact]
        public void CreateNamespaceManager_ReturnsExpectedManager()
        {
            // default connection
            NamespaceManager manager = _provider.CreateNamespaceManager();
            Assert.Equal("default.servicebus.windows.net", manager.Address.Host);

            // override connection
            manager = _provider.CreateNamespaceManager("ServiceBusOverride");
            Assert.Equal("override.servicebus.windows.net", manager.Address.Host);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("AzureWebJobsServiceBusOverride", null);
        }
    }
}
