// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Queue;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Queues
{
    public class QueueListenerTests
    {
        private QueueListener _listener;
        private Mock<QueueProcessor> _mockQueueProcessor;
        private Mock<ITriggerExecutor<IStorageQueueMessage>> _mockTriggerExecutor;
        private StorageQueueMessage _storageMessage;

        public QueueListenerTests()
        {
            CloudQueue queue = new CloudQueue(new Uri("https://test.queue.core.windows.net/testqueue"));
            Mock<IStorageQueue> mockQueue = new Mock<IStorageQueue>(MockBehavior.Strict);
            mockQueue.Setup(p => p.SdkObject).Returns(queue);

            _mockTriggerExecutor = new Mock<ITriggerExecutor<IStorageQueueMessage>>(MockBehavior.Strict);
            Mock<IDelayStrategy> mockDelayStrategy = new Mock<IDelayStrategy>(MockBehavior.Strict);
            Mock<IBackgroundExceptionDispatcher> mockExceptionDispatcher = new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
            TestTraceWriter log = new TestTraceWriter(TraceLevel.Verbose);
            Mock<IQueueProcessorFactory> mockQueueProcessorFactory = new Mock<IQueueProcessorFactory>(MockBehavior.Strict);
            JobHostQueuesConfiguration queuesConfig = new JobHostQueuesConfiguration();
            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(queue, log, queuesConfig);

            _mockQueueProcessor = new Mock<QueueProcessor>(MockBehavior.Strict, context);
            JobHostQueuesConfiguration queueConfig = new JobHostQueuesConfiguration
            {
                MaxDequeueCount = 5,
                QueueProcessorFactory = mockQueueProcessorFactory.Object
            };

            mockQueueProcessorFactory.Setup(p => p.Create(It.IsAny<QueueProcessorFactoryContext>())).Returns(_mockQueueProcessor.Object);

            _listener = new QueueListener(mockQueue.Object, null, _mockTriggerExecutor.Object, mockDelayStrategy.Object, mockExceptionDispatcher.Object, log, null, queueConfig);

            CloudQueueMessage cloudMessage = new CloudQueueMessage("TestMessage");
            _storageMessage = new StorageQueueMessage(cloudMessage);
        }

        [Fact]
        public void CreateQueueProcessor_CreatesProcessorCorrectly()
        {
            CloudQueue poisonQueue = null;
            TestTraceWriter log = new TestTraceWriter(TraceLevel.Verbose);
            bool poisonMessageHandlerInvoked = false;
            EventHandler poisonMessageEventHandler = (sender, e) => { poisonMessageHandlerInvoked = true; };
            Mock<IQueueProcessorFactory> mockQueueProcessorFactory = new Mock<IQueueProcessorFactory>(MockBehavior.Strict);
            JobHostQueuesConfiguration queueConfig = new JobHostQueuesConfiguration
            {
                MaxDequeueCount = 7,
                QueueProcessorFactory = mockQueueProcessorFactory.Object
            };
            QueueProcessor expectedQueueProcessor = null;
            bool processorFactoryInvoked = false;

            // create for a host queue - don't expect custom factory to be invoked
            CloudQueue queue = new CloudQueue(new Uri(string.Format("https://test.queue.core.windows.net/{0}", HostQueueNames.GetHostQueueName("12345"))));
            QueueProcessor queueProcessor = QueueListener.CreateQueueProcessor(queue, poisonQueue, log, queueConfig, poisonMessageEventHandler);
            Assert.False(processorFactoryInvoked);
            Assert.NotSame(expectedQueueProcessor, queueProcessor);
            queueProcessor.OnMessageAddedToPoisonQueue(new EventArgs());
            Assert.True(poisonMessageHandlerInvoked);

            QueueProcessorFactoryContext processorFactoryContext = null;
            mockQueueProcessorFactory.Setup(p => p.Create(It.IsAny<QueueProcessorFactoryContext>()))
                .Callback<QueueProcessorFactoryContext>((mockProcessorContext) =>
                {
                    processorFactoryInvoked = true;

                    Assert.Same(queue, mockProcessorContext.Queue);
                    Assert.Same(poisonQueue, mockProcessorContext.PoisonQueue);
                    Assert.Equal(queueConfig.MaxDequeueCount, mockProcessorContext.MaxDequeueCount);
                    Assert.Same(log, mockProcessorContext.Trace);

                    processorFactoryContext = mockProcessorContext;
                })
                .Returns(() => 
                {
                    expectedQueueProcessor = new QueueProcessor(processorFactoryContext);
                    return expectedQueueProcessor;
                });

            // when storage host is "localhost" we invoke the processor factory even for
            // host queues (this enables local test mocking)
            processorFactoryInvoked = false;
            queue = new CloudQueue(new Uri(string.Format("https://localhost/{0}", HostQueueNames.GetHostQueueName("12345"))));
            queueProcessor = QueueListener.CreateQueueProcessor(queue, poisonQueue, log, queueConfig, poisonMessageEventHandler);
            Assert.True(processorFactoryInvoked);
            Assert.Same(expectedQueueProcessor, queueProcessor);

            // create for application queue - expect processor factory to be invoked
            poisonMessageHandlerInvoked = false;
            processorFactoryInvoked = false;
            queue = new CloudQueue(new Uri("https://test.queue.core.windows.net/testqueue"));
            queueProcessor = QueueListener.CreateQueueProcessor(queue, poisonQueue, log, queueConfig, poisonMessageEventHandler);
            Assert.True(processorFactoryInvoked);
            Assert.Same(expectedQueueProcessor, queueProcessor);
            queueProcessor.OnMessageAddedToPoisonQueue(new EventArgs());
            Assert.True(poisonMessageHandlerInvoked);

            // if poison message watcher not specified, event not subscribed to
            poisonMessageHandlerInvoked = false;
            processorFactoryInvoked = false;
            queueProcessor = QueueListener.CreateQueueProcessor(queue, poisonQueue, log, queueConfig, null);
            Assert.True(processorFactoryInvoked);
            Assert.Same(expectedQueueProcessor, queueProcessor);
            queueProcessor.OnMessageAddedToPoisonQueue(new EventArgs());
            Assert.False(poisonMessageHandlerInvoked);
        }

        [Fact]
        public async Task ProcessMessageAsync_Success()
        {
            CancellationToken cancellationToken = new CancellationToken();
            FunctionResult result = new FunctionResult(true);
            _mockQueueProcessor.Setup(p => p.BeginProcessingMessageAsync(_storageMessage.SdkObject, cancellationToken)).ReturnsAsync(true);
            _mockTriggerExecutor.Setup(p => p.ExecuteAsync(_storageMessage, cancellationToken)).ReturnsAsync(result);
            _mockQueueProcessor.Setup(p => p.CompleteProcessingMessageAsync(_storageMessage.SdkObject, result, cancellationToken)).Returns(Task.FromResult(true));

            await _listener.ProcessMessageAsync(_storageMessage, TimeSpan.FromMinutes(10), cancellationToken);
        }

        [Fact]
        public async Task ProcessMessageAsync_QueueBeginProcessingMessageReturnsFalse_MessageNotProcessed()
        {
            CancellationToken cancellationToken = new CancellationToken();
            _mockQueueProcessor.Setup(p => p.BeginProcessingMessageAsync(_storageMessage.SdkObject, cancellationToken)).ReturnsAsync(false);

            await _listener.ProcessMessageAsync(_storageMessage, TimeSpan.FromMinutes(10), cancellationToken);
        }

        [Fact]
        public async Task ProcessMessageAsync_FunctionInvocationFails()
        {
            CancellationToken cancellationToken = new CancellationToken();
            FunctionResult result = new FunctionResult(false);
            _mockQueueProcessor.Setup(p => p.BeginProcessingMessageAsync(_storageMessage.SdkObject, cancellationToken)).ReturnsAsync(true);
            _mockTriggerExecutor.Setup(p => p.ExecuteAsync(_storageMessage, cancellationToken)).ReturnsAsync(result);
            _mockQueueProcessor.Setup(p => p.CompleteProcessingMessageAsync(_storageMessage.SdkObject, result, cancellationToken)).Returns(Task.FromResult(true));

            await _listener.ProcessMessageAsync(_storageMessage, TimeSpan.FromMinutes(10), cancellationToken);
        }
    }
}
