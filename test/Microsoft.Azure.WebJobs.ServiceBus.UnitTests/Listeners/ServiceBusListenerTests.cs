// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.ServiceBus.Listeners;
using Microsoft.ServiceBus.Messaging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests.Listeners
{
    public class ServiceBusListenerTests
    {
        private readonly MessagingFactory _messagingFactory;
        private readonly ServiceBusListener _listener;
        private readonly Mock<ITriggeredFunctionExecutor> _mockExecutor;
        private readonly Mock<MessagingProvider> _mockMessagingProvider;
        private readonly Mock<MessageProcessor> _mockMessageProcessor;
        private readonly string _entityPath = "test-entity-path";

        public ServiceBusListenerTests()
        {
            _mockExecutor = new Mock<ITriggeredFunctionExecutor>(MockBehavior.Strict);

            string testConnection = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=";
            _messagingFactory = MessagingFactory.CreateFromConnectionString(testConnection);
            OnMessageOptions messageOptions = new OnMessageOptions();
            _mockMessageProcessor = new Mock<MessageProcessor>(MockBehavior.Strict, messageOptions);
            
            ServiceBusConfiguration config = new ServiceBusConfiguration
            {
                MessageOptions = messageOptions
            };
            _mockMessagingProvider = new Mock<MessagingProvider>(MockBehavior.Strict, config);
            config.MessagingProvider = _mockMessagingProvider.Object;

            _mockMessagingProvider.Setup(p => p.CreateMessageProcessor(_entityPath))
                .Returns(_mockMessageProcessor.Object);

            ServiceBusTriggerExecutor triggerExecutor = new ServiceBusTriggerExecutor(_mockExecutor.Object);
            _listener = new ServiceBusListener(_messagingFactory, _entityPath, triggerExecutor, config);
        }

        [Fact]
        public async Task ProcessMessageAsync_Success()
        {
            BrokeredMessage message = new BrokeredMessage();
            CancellationToken cancellationToken = new CancellationToken();
            _mockMessageProcessor.Setup(p => p.BeginProcessingMessageAsync(message, cancellationToken)).ReturnsAsync(true);

            FunctionResult result = new FunctionResult(true);
            _mockExecutor.Setup(p => p.TryExecuteAsync(It.Is<TriggeredFunctionData>(q => q.TriggerValue == message), cancellationToken)).ReturnsAsync(result);

            _mockMessageProcessor.Setup(p => p.CompleteProcessingMessageAsync(message, result, cancellationToken)).Returns(Task.FromResult(0));

            await _listener.ProcessMessageAsync(message, CancellationToken.None);

            _mockMessageProcessor.VerifyAll();
            _mockExecutor.VerifyAll();
            _mockMessageProcessor.VerifyAll();
        }

        [Fact]
        public async Task ProcessMessageAsync_BeginProcessingReturnsFalse_MessageNotProcessed()
        {
            BrokeredMessage message = new BrokeredMessage();
            CancellationToken cancellationToken = new CancellationToken();
            _mockMessageProcessor.Setup(p => p.BeginProcessingMessageAsync(message, cancellationToken)).ReturnsAsync(false);

            await _listener.ProcessMessageAsync(message, CancellationToken.None);

            _mockMessageProcessor.VerifyAll();
        }

        [Fact]
        public async Task StartAsync_CallsMessagingProviderToCreateReceiver()
        {
            MessageReceiver receiver = _messagingFactory.CreateMessageReceiver(_entityPath);
            _mockMessagingProvider.Setup(p => p.CreateMessageReceiver(_messagingFactory, _entityPath)).Returns(receiver);

            MessagingCommunicationException ex = await Assert.ThrowsAsync<MessagingCommunicationException>(async () => 
                {
                    await _listener.StartAsync(CancellationToken.None);
                });
            Assert.Equal("Unable to connect to Service Bus using HTTP connectivity mode.", ex.Message);

            _mockMessagingProvider.VerifyAll();
        }
    }
}
