// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));
            IStorageQueue queue = account.CreateQueueClient().GetQueueReference(QueueName);

            // Act
            CloudQueue result = RunTrigger<CloudQueue>(account, typeof(BindToCloudQueueProgram),
                (s) => BindToCloudQueueProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(QueueName, queue.Name);
            Assert.True(queue.Exists());
        }

        [Fact]
        public void Queue_IfBoundToICollectorCloudQueueMessage_AddEnqueuesMessage()
        {
            // Arrange
            const string expectedContent = "message";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedContent));
            IStorageQueue queue = CreateQueue(account, QueueName);

            // Act
            RunTrigger<object>(account, typeof(BindToICollectorCloudQueueMessageProgram),
                (s) => BindToICollectorCloudQueueMessageProgram.TaskSource = s);

            // Assert
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

        private static IStorageQueue CreateQueue(IStorageAccount account, string queueName)
        {
            IStorageQueueClient client = account.CreateQueueClient();
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
