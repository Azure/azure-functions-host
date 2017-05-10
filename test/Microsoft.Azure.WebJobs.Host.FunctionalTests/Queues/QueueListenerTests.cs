// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.Queues
{
    public class QueueListenerTests : IClassFixture<QueueListenerTests.TestFixture>
    {
        public QueueListenerTests(TestFixture fixture)
        {
            Fixture = fixture;
        }

        public TestFixture Fixture { get; set; }

        [Fact]
        public async Task UpdatedQueueMessage_RetainsOriginalProperties()
        {
            TraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            CloudQueue queue = Fixture.Queue;
            CloudQueue poisonQueue = Fixture.PoisonQueue;
            JobHostQueuesConfiguration queuesConfig = new JobHostQueuesConfiguration { MaxDequeueCount = 2 };
            StorageQueue storageQueue = new StorageQueue(new StorageQueueClient(Fixture.QueueClient), queue);
            StorageQueue storagePoisonQueue = new StorageQueue(new StorageQueueClient(Fixture.QueueClient), poisonQueue);
            Mock<ITriggerExecutor<IStorageQueueMessage>> mockTriggerExecutor = new Mock<ITriggerExecutor<IStorageQueueMessage>>(MockBehavior.Strict);

            Mock<IWebJobsExceptionHandler> mockExceptionDispatcher = new Mock<IWebJobsExceptionHandler>(MockBehavior.Strict);
            mockExceptionDispatcher
                .Setup(m => m.OnUnhandledExceptionAsync(It.IsAny<ExceptionDispatchInfo>()))
                .Callback<ExceptionDispatchInfo>(e =>
                {
                    throw e.SourceException;
                });

            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(queue, trace, null, queuesConfig, poisonQueue: poisonQueue);
            Mock<QueueProcessor> mockProcessor = new Mock<QueueProcessor>(MockBehavior.Strict, context);
            mockProcessor.CallBase = false;

            string messageContent = Guid.NewGuid().ToString();
            CloudQueueMessage message = new CloudQueueMessage(messageContent);
            await queue.AddMessageAsync(message, CancellationToken.None);
            CloudQueueMessage messageFromCloud = await queue.GetMessageAsync();

            TestQueueProcessor processor = new TestQueueProcessor(context);
            processor.Original = messageFromCloud;

            QueueListener listener = new QueueListener(storageQueue, storagePoisonQueue, mockTriggerExecutor.Object, mockExceptionDispatcher.Object, trace,
                null, null, queuesConfig, processor);

            // Set up a function that will put the message in another queue.
            mockTriggerExecutor
                .Setup(m => m.ExecuteAsync(It.Is<IStorageQueueMessage>(msg => msg.DequeueCount == 1), CancellationToken.None))
                .Callback<IStorageQueueMessage, CancellationToken>((msg, t) =>
                {
                    poisonQueue.AddMessage(msg.SdkObject);
                })
                .ReturnsAsync(new FunctionResult(false));

            mockTriggerExecutor
                .Setup(m => m.ExecuteAsync(It.Is<IStorageQueueMessage>(msg => msg.DequeueCount == 2), CancellationToken.None))
                .Callback<IStorageQueueMessage, CancellationToken>((msg, t) =>
                {
                    poisonQueue.AddMessage(msg.SdkObject);
                })
                .ReturnsAsync(new FunctionResult(false));

            await listener.ProcessMessageAsync(new StorageQueueMessage(messageFromCloud), TimeSpan.FromMinutes(10), CancellationToken.None);

            // pull the message and process it again (to have it go through the poison queue flow)
            messageFromCloud = await queue.GetMessageAsync();
            Assert.Equal(2, messageFromCloud.DequeueCount);

            processor.Original = messageFromCloud;

            await listener.ProcessMessageAsync(new StorageQueueMessage(messageFromCloud), TimeSpan.FromMinutes(10), CancellationToken.None);
        }

        private class TestQueueProcessor : QueueProcessor
        {
            private CloudQueue _queue;
            private CloudQueue _poisonQueue;
            private CloudQueueMessage _original;

            // We want to change the content at every step and make sure it flows.
            private string _lastContentString;
            private byte[] _lastContentBytes;

            public TestQueueProcessor(QueueProcessorFactoryContext context)
                : base(context)
            {
                _queue = context.Queue;
                _poisonQueue = context.PoisonQueue;
            }

            public CloudQueueMessage Original
            {
                get { return _original; }
                set
                {
                    _original = value;
                    _lastContentString = _original.AsString;
                    _lastContentBytes = _original.AsBytes;
                }
            }

            public override async Task<bool> BeginProcessingMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
            {
                ValidateMessage(_original, message);
                UpdateMessage(message);

                return await base.BeginProcessingMessageAsync(message, cancellationToken);
            }

            public override async Task CompleteProcessingMessageAsync(CloudQueueMessage message, FunctionResult result, CancellationToken cancellationToken)
            {
                ValidateMessage(_original, message);
                UpdateMessage(message);

                await base.CompleteProcessingMessageAsync(message, result, cancellationToken);
            }

            protected override async Task DeleteMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
            {
                ValidateMessage(_original, message);
                UpdateMessage(message);

                await base.DeleteMessageAsync(message, cancellationToken);
            }

            protected override async Task CopyMessageToPoisonQueueAsync(CloudQueueMessage message, CloudQueue poisonQueue, CancellationToken cancellationToken)
            {
                ValidateMessage(_original, message);
                UpdateMessage(message);

                await base.CopyMessageToPoisonQueueAsync(message, poisonQueue, cancellationToken);
            }

            protected override async Task ReleaseMessageAsync(CloudQueueMessage message, FunctionResult result, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
            {
                ValidateMessage(_original, message);
                UpdateMessage(message);

                await base.ReleaseMessageAsync(message, result, visibilityTimeout, cancellationToken);
            }

            private void UpdateMessage(CloudQueueMessage message)
            {
                // We want to make sure that changes to the message flow through to the 
                // other virtual methods.
                _lastContentString = Guid.NewGuid().ToString();
                message.SetMessageContent(_lastContentString);
                _lastContentBytes = message.AsBytes;
            }

            private void ValidateMessage(CloudQueueMessage original, CloudQueueMessage cloned)
            {
                Assert.Equal(_lastContentBytes, cloned.AsBytes);
                Assert.Equal(_lastContentString, cloned.AsString);
                Assert.Equal(original.DequeueCount, cloned.DequeueCount);
                Assert.Equal(original.ExpirationTime, cloned.ExpirationTime);
                Assert.Equal(original.Id, cloned.Id);
                Assert.Equal(original.InsertionTime, cloned.InsertionTime);
                Assert.Equal(original.NextVisibleTime, cloned.NextVisibleTime);
                Assert.Equal(original.PopReceipt, cloned.PopReceipt);
            }
        }

        public class TestFixture : IDisposable
        {
            private const string TestQueuePrefix = "queuelistenertests";

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
