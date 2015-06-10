// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.ServiceBus.Listeners;
using Microsoft.ServiceBus.Messaging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Listeners
{
    public class ServiceBusSubscriptionListenerFactoryTests
    {
        [Theory]
        [InlineData(AccessRights.Listen)]
        [InlineData(AccessRights.Send)]
        public async Task CreateAsync_AccessRightsNotManage_DoesNotCreateTopicOrSubscription(AccessRights accessRights)
        {
            ServiceBusAccount account = new ServiceBusAccount();
            Mock<ITriggeredFunctionExecutor<BrokeredMessage>> mockExecutor = new Mock<ITriggeredFunctionExecutor<BrokeredMessage>>(MockBehavior.Strict);
            ServiceBusSubscriptionListenerFactory factory = new ServiceBusSubscriptionListenerFactory(account, "testtopic", "testsubscription", mockExecutor.Object, accessRights);

            ListenerFactoryContext context = new ListenerFactoryContext(CancellationToken.None);
            IListener listener = await factory.CreateAsync(context);
            Assert.NotNull(listener);
        }
    }
}
