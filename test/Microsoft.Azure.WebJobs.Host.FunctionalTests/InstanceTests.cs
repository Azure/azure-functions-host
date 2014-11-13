// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class InstanceTests
    {
        private const string QueueName = "input";

        [Fact]
        public void Trigger_CanBeInstanceMethod()
        {
            // Arrange
            CloudQueueMessage expectedMessage = new CloudQueueMessage("ignore");
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(new FakeStorageQueueMessage(expectedMessage));

            // Act
            CloudQueueMessage result = RunTrigger<CloudQueueMessage>(account, typeof(BindToCloudQueueMessageProgram),
                (s) => BindToCloudQueueMessageProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        private class BindToCloudQueueMessageProgram
        {
            public static TaskCompletionSource<CloudQueueMessage> TaskSource { get; set; }

            public void Run([QueueTrigger(QueueName)] CloudQueueMessage message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        [Fact]
        public void Trigger_CanBeAsyncInstanceMethod()
        {
            // Arrange
            CloudQueueMessage expectedMessage = new CloudQueueMessage("ignore");
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(new FakeStorageQueueMessage(expectedMessage));

            // Act
            CloudQueueMessage result = RunTrigger<CloudQueueMessage>(account,
                typeof(BindToCloudQueueMessageAsyncProgram), (s) => BindToCloudQueueMessageAsyncProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        private class BindToCloudQueueMessageAsyncProgram
        {
            public static TaskCompletionSource<CloudQueueMessage> TaskSource { get; set; }

            public Task RunAsync([QueueTrigger(QueueName)] CloudQueueMessage message)
            {
                TaskSource.TrySetResult(message);
                return Task.FromResult(0);
            }
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
    }
}
