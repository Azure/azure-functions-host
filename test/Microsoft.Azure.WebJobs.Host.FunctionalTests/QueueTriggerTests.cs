// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class QueueTriggerTests
    {
        private const string QueueName = "input";

        [Fact]
        public void CanBindToCloudQueueMessage()
        {
            // Arrange
            CloudQueueMessage expectedMessage = new CloudQueueMessage("ignore");
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(new SdkStorageQueueMessage(expectedMessage));

            // Act
            CloudQueueMessage result = RunQueueTrigger<CloudQueueMessage>(account,
                typeof(BindToCloudQueueMessageProgram), (s) => BindToCloudQueueMessageProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        [Fact]
        public void CanBindToString()
        {
            const string expectedContent = "abc";
            TestBindToString(expectedContent);
        }

        [Fact]
        public void CanBindToEmptyString()
        {
            TestBindToString(String.Empty);
        }

        private static void TestBindToString(string expectedContent)
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage(expectedContent);
            queue.AddMessage(message);

            // Act
            string result = RunQueueTrigger<string>(account, typeof(BindToStringProgram),
                (s) => BindToStringProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedContent, result);
        }

        private static TResult RunQueueTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            // Arrange
            TaskCompletionSource<TResult> taskSource = new TaskCompletionSource<TResult>();
            IServiceProvider serviceProvider = CreateServiceProvider<TResult>(account, programType, taskSource);
            Task<TResult> task = taskSource.Task;
            setTaskSource.Invoke(taskSource);
            bool completed;

            using (JobHost host = new JobHost(serviceProvider))
            {
                try
                {
                    host.Start();

                    // Act
                    completed = task.WaitUntilCompleted(3 * 1000);
                }
                finally
                {
                    setTaskSource.Invoke(null);
                }
            }

            // Assert
            Assert.True(completed);

            // Give a nicer test failure message for faulted tasks.
            if (task.Status == TaskStatus.Faulted)
            {
                task.GetAwaiter().GetResult();
            }

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            return task.Result;
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

        private static IServiceProvider CreateServiceProvider<TResult>(IStorageAccount storageAccount, Type programType,
            TaskCompletionSource<TResult> taskSource)
        {
            return new FakeServiceProvider
            {
                StorageAccountProvider = new FakeStorageAccountProvider
                {
                    StorageAccount = storageAccount
                },
                TypeLocator = new FakeTypeLocator(programType),
                BackgroundExceptionDispatcher = new TaskBackgroundExceptionDispatcher<TResult>(taskSource),
                HostInstanceLogger = new NullHostInstanceLogger(),
                FunctionInstanceLogger = new TaskFunctionInstanceLogger<TResult>(taskSource),
                ConnectionStringProvider = new NullConnectionStringProvider(),
                HostIdProvider = new FakeHostIdProvider(),
                QueueConfiguration = new FakeQueueConfiguration(),
                StorageCredentialsValidator = new FakeStorageCredentialsValidator()
            };
        }

        private class BindToCloudQueueMessageProgram
        {
            public static TaskCompletionSource<CloudQueueMessage> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] CloudQueueMessage message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        private class BindToStringProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] string message)
            {
                TaskSource.TrySetResult(message);
            }
        }
    }
}
