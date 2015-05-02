// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.ServiceBus.Config;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Extension methods for ServiceBus integration
    /// </summary>
    public static class JobHostConfigurationExtensions
    {
        /// <summary>
        /// Enables use of ServiceBus job extensions
        /// </summary>
        /// <param name="config">The <see cref="JobHostConfiguration"/> to configure.</param>
        public static void UseServiceBus(this JobHostConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            ServiceBusConfiguration serviceBusConfig = new ServiceBusConfiguration
            {
                // Can remove this pragma in a future release after
                // JobHostConfiguration.ServiceBusConnectionString is removed
                #pragma warning disable 0618
                ConnectionString = config.ServiceBusConnectionString
                #pragma warning restore 0618
            };

            config.UseServiceBus(serviceBusConfig);
        }

        /// <summary>
        /// Enables use of ServiceBus job extensions
        /// </summary>
        /// <param name="config">The <see cref="JobHostConfiguration"/> to configure.</param>
        /// <param name="serviceBusConfig">The <see cref="ServiceBusConfiguration"></see> to use./></param>
        public static void UseServiceBus(this JobHostConfiguration config, ServiceBusConfiguration serviceBusConfig)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (serviceBusConfig == null)
            {
                throw new ArgumentNullException("serviceBusConfig");
            }

            ServiceBusExtensionConfig extensionConfig = new ServiceBusExtensionConfig(config, serviceBusConfig);

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();
            extensions.RegisterExtension<IExtensionConfigProvider>(extensionConfig);
        }
    }
}
