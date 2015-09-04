// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.ServiceBus.Triggers;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Bindings
{
    public class ServiceBusTriggerAttributeBindingProviderTests
    {
        private readonly Mock<MessagingProvider> _mockMessagingProvider;
        private readonly ServiceBusTriggerAttributeBindingProvider _provider;

        public ServiceBusTriggerAttributeBindingProviderTests()
        {
            Mock<INameResolver> mockResolver = new Mock<INameResolver>(MockBehavior.Strict);

            ServiceBusConfiguration config = new ServiceBusConfiguration();
            _mockMessagingProvider = new Mock<MessagingProvider>(MockBehavior.Strict, config);

            config.MessagingProvider = _mockMessagingProvider.Object;
            _provider = new ServiceBusTriggerAttributeBindingProvider(mockResolver.Object, config);
        }

        [Fact]
        public async Task TryCreateAsync_AccountOverride_OverrideIsApplied()
        {
            _mockMessagingProvider.Setup(p => p.CreateNamespaceManager("testaccount")).Returns<NamespaceManager>(null);
            _mockMessagingProvider.Setup(p => p.CreateMessagingFactoryAsync("test", "testaccount")).ReturnsAsync(null);

            ParameterInfo parameter = GetType().GetMethod("TestJob_AccountOverride").GetParameters()[0];
            TriggerBindingProviderContext context = new TriggerBindingProviderContext(parameter, CancellationToken.None);

            ITriggerBinding binding = await _provider.TryCreateAsync(context);

            _mockMessagingProvider.VerifyAll();
        }

        [Fact]
        public async Task TryCreateAsync_DefaultAccount()
        {
            _mockMessagingProvider.Setup(p => p.CreateNamespaceManager(null)).Returns<NamespaceManager>(null);
            _mockMessagingProvider.Setup(p => p.CreateMessagingFactoryAsync("test", null)).ReturnsAsync(null);

            ParameterInfo parameter = GetType().GetMethod("TestJob").GetParameters()[0];
            TriggerBindingProviderContext context = new TriggerBindingProviderContext(parameter, CancellationToken.None);

            ITriggerBinding binding = await _provider.TryCreateAsync(context);

            _mockMessagingProvider.VerifyAll();
        }

        public static void TestJob_AccountOverride(
            [ServiceBusTriggerAttribute("test"),
             ServiceBusAccount("testaccount")] BrokeredMessage message)
        {
            message = new BrokeredMessage();
        }

        public static void TestJob(
            [ServiceBusTriggerAttribute("test")] BrokeredMessage message)
        {
            message = new BrokeredMessage();
        }
    }
}
