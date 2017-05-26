// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
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
            CloudQueue queue = Fixture.CreateNewQueue();
            CloudQueue poisonQueue = Fixture.CreateNewQueue();

            JobHostQueuesConfiguration queuesConfig = new JobHostQueuesConfiguration { MaxDequeueCount = 2 };

            StorageQueue storageQueue = new StorageQueue(new StorageQueueClient(Fixture.QueueClient), queue);
            StorageQueue storagePoisonQueue = new StorageQueue(new StorageQueueClient(Fixture.QueueClient), poisonQueue);
            Mock<ITriggerExecutor<IStorageQueueMessage>> mockTriggerExecutor = new Mock<ITriggerExecutor<IStorageQueueMessage>>(MockBehavior.Strict);

            string messageContent = Guid.NewGuid().ToString();
            CloudQueueMessage message = new CloudQueueMessage(messageContent);
            await queue.AddMessageAsync(message, null, null, null, null, CancellationToken.None);
            CloudQueueMessage messageFromCloud = await queue.GetMessageAsync();

            QueueListener listener = new QueueListener(storageQueue, storagePoisonQueue, mockTriggerExecutor.Object, new WebJobsExceptionHandler(), trace,
                null, null, queuesConfig);

            mockTriggerExecutor
                .Setup(m => m.ExecuteAsync(It.IsAny<IStorageQueueMessage>(), CancellationToken.None))
                .ReturnsAsync(new FunctionResult(false));

            await listener.ProcessMessageAsync(new StorageQueueMessage(messageFromCloud), TimeSpan.FromMinutes(10), CancellationToken.None);

            // pull the message and process it again (to have it go through the poison queue flow)
            messageFromCloud = await queue.GetMessageAsync();
            Assert.Equal(2, messageFromCloud.DequeueCount);

            await listener.ProcessMessageAsync(new StorageQueueMessage(messageFromCloud), TimeSpan.FromMinutes(10), CancellationToken.None);

            // Make sure the message was processed and deleted.
            await queue.FetchAttributesAsync();
            Assert.Equal(0, queue.ApproximateMessageCount);

            // The Listener has inserted a message to the poison queue.
            await poisonQueue.FetchAttributesAsync();
            Assert.Equal(1, poisonQueue.ApproximateMessageCount);

            mockTriggerExecutor.Verify(m => m.ExecuteAsync(It.IsAny<IStorageQueueMessage>(), CancellationToken.None), Times.Exactly(2));
        }

        [Fact]
        public async Task RenewedQueueMessage_DeletesCorrectly()
        {
            TraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            CloudQueue queue = Fixture.CreateNewQueue();

            StorageQueue storageQueue = new StorageQueue(new StorageQueueClient(Fixture.QueueClient), queue);
            Mock<ITriggerExecutor<IStorageQueueMessage>> mockTriggerExecutor = new Mock<ITriggerExecutor<IStorageQueueMessage>>(MockBehavior.Strict);

            string messageContent = Guid.NewGuid().ToString();
            CloudQueueMessage message = new CloudQueueMessage(messageContent);
            await queue.AddMessageAsync(message, null, null, null, null, CancellationToken.None);
            CloudQueueMessage messageFromCloud = await queue.GetMessageAsync();

            QueueListener listener = new QueueListener(storageQueue, null, mockTriggerExecutor.Object, new WebJobsExceptionHandler(), trace,
                null, null, new JobHostQueuesConfiguration());
            listener.MinimumVisibilityRenewalInterval = TimeSpan.FromSeconds(1);

            // Set up a function that sleeps to allow renewal
            mockTriggerExecutor
                .Setup(m => m.ExecuteAsync(It.Is<IStorageQueueMessage>(msg => msg.DequeueCount == 1), CancellationToken.None))
                .ReturnsAsync(() =>
                {
                    Thread.Sleep(4000);
                    return new FunctionResult(true);
                });

            var previousNextVisibleTime = messageFromCloud.NextVisibleTime;
            var previousPopReceipt = messageFromCloud.PopReceipt;

            // Renewal should happen at 2 seconds
            await listener.ProcessMessageAsync(new StorageQueueMessage(messageFromCloud), TimeSpan.FromSeconds(4), CancellationToken.None);

            // Check to make sure the renewal occurred.
            Assert.NotEqual(messageFromCloud.NextVisibleTime, previousNextVisibleTime);
            Assert.NotEqual(messageFromCloud.PopReceipt, previousPopReceipt);

            // Make sure the message was processed and deleted.
            await queue.FetchAttributesAsync();
            Assert.Equal(0, queue.ApproximateMessageCount);
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
                Queue.CreateIfNotExistsAsync(null, null, CancellationToken.None).Wait();

                string poisonQueueName = string.Format("{0}-poison", queueName);
                PoisonQueue = client.GetQueueReference(poisonQueueName).SdkObject;
                PoisonQueue.CreateIfNotExistsAsync(null, null, CancellationToken.None).Wait();
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

            public CloudQueue CreateNewQueue()
            {
                string queueName = string.Format("{0}-{1}", TestQueuePrefix, Guid.NewGuid());
                var queue = QueueClient.GetQueueReference(queueName);
                queue.CreateIfNotExistsAsync(null, null, CancellationToken.None).Wait();
                return queue;
            }

            public void Dispose()
            {

                var result = QueueClient.ListQueuesSegmentedAsync(TestQueuePrefix, null).Result;
                var tasks = new List<Task>();

                foreach (var queue in result.Results)
                {
                    tasks.Add(queue.DeleteAsync());
                }

                Task.WaitAll(tasks.ToArray());
            }
        }
    }
}
