// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.ServiceBus.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Config
{
    public class JobHostConfigurationExtensionsTests
    {
        [Fact]
        public void UseServiceBus_ThrowsArgumentNull_WhenServiceBusConfigIsNull()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                {
                    config.UseServiceBus(null);
                });
            Assert.Equal("serviceBusConfig", exception.ParamName);
        }

        [Fact]
        public void UseServiceBus_NoServiceBusConfiguration_PerformsExpectedRegistration()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            string serviceBusConnection = "test service bus connection";
#pragma warning disable 0618
            config.ServiceBusConnectionString = serviceBusConnection;
#pragma warning restore 0618

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();
            IExtensionConfigProvider[] configProviders = extensions.GetExtensions<IExtensionConfigProvider>().ToArray();
            Assert.Equal(0, configProviders.Length);

            config.UseServiceBus();

            // verify that the service bus config provider was registered
            configProviders = extensions.GetExtensions<IExtensionConfigProvider>().ToArray();
            Assert.Equal(1, configProviders.Length);

            ServiceBusExtensionConfig serviceBusExtensionConfig = (ServiceBusExtensionConfig)configProviders.Single();

            // verify that a default ServiceBusConfiguration was created, with the host (obsolete)
            // service bus connection string propagated
#pragma warning disable 0618
            Assert.Equal(serviceBusConnection, serviceBusExtensionConfig.Config.ConnectionString);
#pragma warning restore 0618
        }

        [Fact]
        public void UseServiceBus_ServiceBusConfigurationProvided_PerformsExpectedRegistration()
        {
            JobHostConfiguration config = new JobHostConfiguration();

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();
            IExtensionConfigProvider[] configProviders = extensions.GetExtensions<IExtensionConfigProvider>().ToArray();
            Assert.Equal(0, configProviders.Length);

            ServiceBusConfiguration serviceBusConfig = new ServiceBusConfiguration
            {
                ConnectionString = "test service bus connection"
            };
            config.UseServiceBus(serviceBusConfig);

            // verify that the service bus config provider was registered
            configProviders = extensions.GetExtensions<IExtensionConfigProvider>().ToArray();
            Assert.Equal(1, configProviders.Length);

            ServiceBusExtensionConfig serviceBusExtensionConfig = (ServiceBusExtensionConfig)configProviders.Single();
            Assert.Same(serviceBusConfig, serviceBusExtensionConfig.Config);
        }
    }
}
