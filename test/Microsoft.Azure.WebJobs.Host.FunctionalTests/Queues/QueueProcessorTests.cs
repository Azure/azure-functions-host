// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    [Trait("SecretsRequired", "true")]
    public class QueueProcessorTests : IClassFixture<QueueProcessorTests.TestFixture>
    {
        private CloudQueue _queue;
        private CloudQueue _poisonQueue;
        private QueueProcessor _processor;
        private TraceWriter _trace;
        private JobHostQueuesConfiguration _queuesConfig;

        public QueueProcessorTests(TestFixture fixture)
        {
            _trace = new TestTraceWriter(TraceLevel.Verbose);
            _queue = fixture.Queue;
            _poisonQueue = fixture.PoisonQueue;

            _queuesConfig = new JobHostQueuesConfiguration();
            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(_queue, _trace, null, _queuesConfig);
            _processor = new QueueProcessor(context);
        }

        [Fact]
        public void Constructor_DefaultsValues()
        {
            var config = new JobHostQueuesConfiguration
            {
                BatchSize = 32,
                MaxDequeueCount = 2,
                NewBatchThreshold = 100,
                VisibilityTimeout = TimeSpan.FromSeconds(30),
                MaxPollingInterval = TimeSpan.FromSeconds(15)
            };
            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(_queue, _trace, null, config);
            QueueProcessor localProcessor = new QueueProcessor(context);

            Assert.Equal(config.BatchSize, localProcessor.BatchSize);
            Assert.Equal(config.MaxDequeueCount, localProcessor.MaxDequeueCount);
            Assert.Equal(config.NewBatchThreshold, localProcessor.NewBatchThreshold);
            Assert.Equal(config.VisibilityTimeout, localProcessor.VisibilityTimeout);
            Assert.Equal(config.MaxPollingInterval, localProcessor.MaxPollingInterval);
        }

        [Fact]
        public async Task CompleteProcessingMessageAsync_Success_DeletesMessage()
        {
            CloudQueueMessage message = new CloudQueueMessage("Test Message");
            await _queue.AddMessageAsync(message, CancellationToken.None);

            message = _queue.GetMessage();

            FunctionResult result = new FunctionResult(true);
            await _processor.CompleteProcessingMessageAsync(message, result, CancellationToken.None);

            message = await _queue.GetMessageAsync();
            Assert.Null(message);
        }

        [Fact]
        public async Task CompleteProcessingMessageAsync_FailureWithoutPoisonQueue_DoesNotDeleteMessage()
        {
            CloudQueueMessage message = new CloudQueueMessage("Test Message");
            await _queue.AddMessageAsync(message, CancellationToken.None);

            message = _queue.GetMessage();
            string id = message.Id;

            FunctionResult result = new FunctionResult(false);
            await _processor.CompleteProcessingMessageAsync(message, result, CancellationToken.None);

            // make the message visible again so we can verify it wasn't deleted
            await _queue.UpdateMessageAsync(message, TimeSpan.Zero, MessageUpdateFields.Visibility);

            message = await _queue.GetMessageAsync();
            Assert.NotNull(message);
            Assert.Equal(id, message.Id);
        }

        [Fact]
        public async Task CompleteProcessingMessageAsync_MaxDequeueCountExceeded_MovesMessageToPoisonQueue()
        {
            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(_queue, _trace, null, _queuesConfig, _poisonQueue);
            QueueProcessor localProcessor = new QueueProcessor(context);

            bool poisonMessageHandlerCalled = false;
            localProcessor.MessageAddedToPoisonQueue += (sender, e) =>
                {
                    Assert.Same(sender, localProcessor);
                    Assert.Same(_poisonQueue, e.PoisonQueue);
                    Assert.NotNull(e.Message);
                    poisonMessageHandlerCalled = true;
                };

            string messageContent = Guid.NewGuid().ToString();
            CloudQueueMessage message = new CloudQueueMessage(messageContent);
            await _queue.AddMessageAsync(message, CancellationToken.None);

            FunctionResult result = new FunctionResult(false);
            for (int i = 0; i < context.MaxDequeueCount; i++)
            {
                message = await _queue.GetMessageAsync();
                await localProcessor.CompleteProcessingMessageAsync(message, result, CancellationToken.None);
            }

            message = await _queue.GetMessageAsync();
            Assert.Null(message);

            CloudQueueMessage poisonMessage = await _poisonQueue.GetMessageAsync();
            Assert.NotNull(poisonMessage);
            Assert.Equal(messageContent, poisonMessage.AsString);
            Assert.True(poisonMessageHandlerCalled);
        }

        [Fact]
        public async Task CompleteProcessingMessageAsync_Failure_AppliesVisibilityTimeout()
        {
            var queuesConfig = new JobHostQueuesConfiguration
            {
                // configure a non-zero visibility timeout
                VisibilityTimeout = TimeSpan.FromMinutes(5)
            };
            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(_queue, _trace, null, queuesConfig, _poisonQueue);
            QueueProcessor localProcessor = new QueueProcessor(context);

            string messageContent = Guid.NewGuid().ToString();
            CloudQueueMessage message = new CloudQueueMessage(messageContent);
            await _queue.AddMessageAsync(message, CancellationToken.None);

            var functionResult = new FunctionResult(false);
            message = await _queue.GetMessageAsync();
            await localProcessor.CompleteProcessingMessageAsync(message, functionResult, CancellationToken.None);

            var delta = message.NextVisibleTime - DateTime.UtcNow;
            Assert.True(delta.Value.TotalMinutes > 4);
        }

        public class TestFixture : IDisposable
        {
            private const string TestQueuePrefix = "queueprocessortests";

            public TestFixture()
            {
                Mock<IServiceProvider> services = new Mock<IServiceProvider>(MockBehavior.Strict);
                StorageClientFactory clientFactory = new StorageClientFactory();
                services.Setup(p => p.GetService(typeof(StorageClientFactory))).Returns(clientFactory);

                DefaultStorageAccountProvider accountProvider = new DefaultStorageAccountProvider(services.Object);
                var task = accountProvider.GetStorageAccountAsync(CancellationToken.None);
                IStorageQueueClient client = task.Result.CreateQueueClient();
                QueueClient = client.SdkObject;

                string queueName = string.Format("{0}-{1}", TestQueuePrefix, Guid.NewGuid());
                Queue = client.GetQueueReference(queueName).SdkObject;
                Queue.CreateIfNotExistsAsync(CancellationToken.None).Wait();

                string poisonQueueName = string.Format("{0}-poison", queueName);
                PoisonQueue = client.GetQueueReference(poisonQueueName).SdkObject;
                PoisonQueue.CreateIfNotExistsAsync(CancellationToken.None).Wait();
            }

            public CloudQueue Queue
            {
                get;
                private set;
            }

            public CloudQueue PoisonQueue
            {
                get;
                private set;
            }

            public CloudQueueClient QueueClient
            {
                get;
                private set;
            }

            public void Dispose()
            {
                foreach (var queue in QueueClient.ListQueues(TestQueuePrefix))
                {
                    queue.Delete();
                }
            }
        }
    }
}
