// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class QueueTests
    {
        private const string TriggerQueueName = "input";
        private const string QueueName = "output";

        [Fact]
        public void Queue_IfBoundToCloudQueue_BindsAndCreatesQueue()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue triggerQueue = CreateQueue(client, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            CloudQueue result = RunTrigger<CloudQueue>(account, typeof(BindToCloudQueueProgram),
                (s) => BindToCloudQueueProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(QueueName, result.Name);
            IStorageQueue queue = client.GetQueueReference(QueueName);
            Assert.True(queue.Exists());
        }

        [Fact]
        public void Queue_IfBoundToICollectorCloudQueueMessage_AddEnqueuesMessage()
        {
            // Arrange
            const string expectedContent = "message";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue triggerQueue = CreateQueue(client, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedContent));

            // Act
            RunTrigger<object>(account, typeof(BindToICollectorCloudQueueMessageProgram),
                (s) => BindToICollectorCloudQueueMessageProgram.TaskSource = s);

            // Assert
            IStorageQueue queue = client.GetQueueReference(QueueName);
            IEnumerable<IStorageQueueMessage> messages = queue.GetMessages(messageCount: 10);
            Assert.NotNull(messages);
            Assert.Equal(1, messages.Count());
            IStorageQueueMessage message = messages.Single();
            Assert.Same(expectedContent, message.AsString);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private static IStorageQueue CreateQueue(IStorageQueueClient client, string queueName)
        {
            IStorageQueue queue = client.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private class BindToCloudQueueProgram
        {
            public static TaskCompletionSource<CloudQueue> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Queue(QueueName)] CloudQueue queue)
            {
                TaskSource.TrySetResult(queue);
            }
        }

        private class BindToICollectorCloudQueueMessageProgram
        {
            public static TaskCompletionSource<object> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage message,
                [Queue(QueueName)] ICollector<CloudQueueMessage> queue)
            {
                queue.Add(message);
                TaskSource.TrySetResult(null);
            }
        }
    }
}
