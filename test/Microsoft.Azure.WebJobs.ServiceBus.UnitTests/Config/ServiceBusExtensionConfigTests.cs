// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.ServiceBus.Bindings;
using Microsoft.Azure.WebJobs.ServiceBus.Config;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Config
{
    public class ServiceBusExtensionConfigTests
    {
        [Fact]
        public void Initialize_PerformsExpectedRegistrations()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            config.AddService<INameResolver>(new RandomNameResolver());

            ServiceBusConfiguration serviceBusConfig = new ServiceBusConfiguration();
            ServiceBusExtensionConfig serviceBusExtensionConfig = new ServiceBusExtensionConfig(config, serviceBusConfig);

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();
            ITriggerBindingProvider[] triggerBindingProviders = extensions.GetExtensions<ITriggerBindingProvider>().ToArray();
            Assert.Equal(0, triggerBindingProviders.Length);
            IBindingProvider[] bindingProviders = extensions.GetExtensions<IBindingProvider>().ToArray();
            Assert.Equal(0, bindingProviders.Length);

            serviceBusExtensionConfig.Initialize();

            // ensure the ServiceBusTriggerAttributeBindingProvider was registered
            triggerBindingProviders = extensions.GetExtensions<ITriggerBindingProvider>().ToArray();
            Assert.Equal(1, triggerBindingProviders.Length);
            ServiceBusTriggerAttributeBindingProvider triggerBindingProvider = (ServiceBusTriggerAttributeBindingProvider)triggerBindingProviders[0];
            Assert.NotNull(triggerBindingProvider);

            // ensure the ServiceBusAttributeBindingProvider was registered
            bindingProviders = extensions.GetExtensions<IBindingProvider>().ToArray();
            Assert.Equal(1, bindingProviders.Length);
            ServiceBusAttributeBindingProvider bindingProvider = (ServiceBusAttributeBindingProvider)bindingProviders[0];
            Assert.NotNull(bindingProvider);
        }
    }
}
