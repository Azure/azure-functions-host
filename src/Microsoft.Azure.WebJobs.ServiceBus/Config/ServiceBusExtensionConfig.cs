// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.ServiceBus.Bindings;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;

namespace Microsoft.Azure.WebJobs.ServiceBus.Config
{
    /// <summary>
    /// Extension configuration provider used to register ServiceBus triggers and binders
    /// </summary>
    internal class ServiceBusExtensionConfig : IExtensionConfigProvider
    {
        private JobHostConfiguration _config;
        private ServiceBusConfiguration _serviceBusConfig;

        /// <summary>
        /// Creates a new <see cref="ServiceBusExtensionConfig"/> instance.
        /// </summary>
        /// <param name="config">The <see cref="JobHostConfiguration"/> to configure.</param>
        /// <param name="serviceBusConfig">The <see cref="ServiceBusConfiguration"></see> to use./></param>
        public ServiceBusExtensionConfig(JobHostConfiguration config, ServiceBusConfiguration serviceBusConfig)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }
            if (serviceBusConfig == null)
            {
                throw new ArgumentNullException("serviceBusConfig");
            }

            _config = config;
            _serviceBusConfig = serviceBusConfig;
        }

        /// <summary>
        /// Gets the <see cref="JobHostConfiguration"/>
        /// </summary>
        public JobHostConfiguration HostConfiguration
        {
            get
            {
                return _config;
            }
        }

        /// <summary>
        /// Gets the <see cref="ServiceBusConfiguration"/>
        /// </summary>
        public ServiceBusConfiguration Config
        {
            get
            {
                return _serviceBusConfig;
            }
        }

        /// <inheritdoc />
        public void Initialize()
        {
            // get the services we need to construct our binding providers
            INameResolver nameResolver = _config.GetService<INameResolver>();
            IExtensionRegistry extensions = _config.GetService<IExtensionRegistry>();

            // register our trigger binding provider
            ServiceBusTriggerAttributeBindingProvider triggerBindingProvider = new ServiceBusTriggerAttributeBindingProvider(nameResolver, _serviceBusConfig);
            extensions.RegisterExtension<ITriggerBindingProvider>(triggerBindingProvider);

            // register our binding provider
            ServiceBusAttributeBindingProvider bindingProvider = new ServiceBusAttributeBindingProvider(nameResolver, _serviceBusConfig);
            extensions.RegisterExtension<IBindingProvider>(bindingProvider);
        }
    }
}
