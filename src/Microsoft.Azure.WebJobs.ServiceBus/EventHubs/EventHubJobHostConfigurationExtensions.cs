// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using System;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Extension for registering an event hub configuration with the JobHostConfiguration.
    /// </summary>
    public static class EventHubJobHostConfigurationExtensions
    {
        /// <summary>
        /// Enable connecting to event hubs for sending and receiving events. This call is required to the <see cref="EventHubAttribute"/> and <see cref="EventHubTriggerAttribute"/> attributes on parameter bindings.
        /// </summary>
        /// <param name="config">job host configuration</param>
        /// <param name="eventHubConfig">event hub configuration contianing connection strings to the event hubs.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static void UseEventHub(this JobHostConfiguration config, EventHubConfiguration eventHubConfig)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (eventHubConfig == null)
            {
                throw new ArgumentNullException("eventHubConfig");
            }

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();
            extensions.RegisterExtension<IExtensionConfigProvider>(eventHubConfig);
        }
    }
}