// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.ServiceBus.Bindings;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Bindings
{
    public class ServiceBusAttributeBindingProviderTests
    {
        private readonly Mock<MessagingProvider> _mockMessagingProvider;
        private readonly ServiceBusAttributeBindingProvider _provider;

        public ServiceBusAttributeBindingProviderTests()
        {
            Mock<INameResolver> mockResolver = new Mock<INameResolver>(MockBehavior.Strict);

            ServiceBusConfiguration config = new ServiceBusConfiguration();
            _mockMessagingProvider = new Mock<MessagingProvider>(MockBehavior.Strict, config);
            
            config.MessagingProvider = _mockMessagingProvider.Object;
            _provider = new ServiceBusAttributeBindingProvider(mockResolver.Object, config);
        }

        [Fact]
        public async Task TryCreateAsync_AccountOverride_OverrideIsApplied()
        {
            _mockMessagingProvider.Setup(p => p.CreateNamespaceManager("testaccount")).Returns<NamespaceManager>(null);
            _mockMessagingProvider.Setup(p => p.CreateMessagingFactory("test", "testaccount")).Returns<MessagingFactory>(null);

            ParameterInfo parameter = GetType().GetMethod("TestJob_AccountOverride").GetParameters()[0];
            BindingProviderContext context = new BindingProviderContext(parameter, new Dictionary<string, Type>(), CancellationToken.None);

            IBinding binding = await _provider.TryCreateAsync(context);

            _mockMessagingProvider.VerifyAll();
        }

        [Fact]
        public async Task TryCreateAsync_DefaultAccount()
        {
            _mockMessagingProvider.Setup(p => p.CreateNamespaceManager(null)).Returns<NamespaceManager>(null);
            _mockMessagingProvider.Setup(p => p.CreateMessagingFactory("test", null)).Returns<MessagingFactory>(null);

            ParameterInfo parameter = GetType().GetMethod("TestJob").GetParameters()[0];
            BindingProviderContext context = new BindingProviderContext(parameter, new Dictionary<string, Type>(), CancellationToken.None);

            IBinding binding = await _provider.TryCreateAsync(context);

            _mockMessagingProvider.VerifyAll();
        }

        public static void TestJob_AccountOverride(
            [ServiceBusAttribute("test"), 
             ServiceBusAccount("testaccount")] out BrokeredMessage message)
        {
            message = new BrokeredMessage();
        }

        public static void TestJob(
            [ServiceBusAttribute("test")] out BrokeredMessage message)
        {
            message = new BrokeredMessage();
        }
    }
}
