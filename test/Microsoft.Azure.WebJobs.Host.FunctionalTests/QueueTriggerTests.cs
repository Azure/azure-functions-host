// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class QueueTriggerTests
    {
        [Fact]
        public void BindToString()
        {
            // Arrange
            const string expectedContent = "abc";
            IStorageAccount account = new FakeStorageAccount();
            IStorageQueue queue = account.CreateQueueClient().GetQueueReference(BindToStringProgram.QueueName);
            queue.CreateIfNotExists();
            IStorageQueueMessage message = queue.CreateMessage(expectedContent);
            queue.AddMessage(message);
            IServiceProvider serviceProvider = CreateServiceProvider(account, typeof(BindToStringProgram));
            TaskCompletionSource<string> taskSource = new TaskCompletionSource<string>();

            using (JobHost host = new JobHost(serviceProvider))
            {
                BindToStringProgram.TaskSource = taskSource;

                try
                {
                    host.Start();
                    Task<string> task = taskSource.Task;

                    // Act
                    task.WaitUntilCompleted(3 * 1000);

                    // Assert
                    Assert.Equal(TaskStatus.RanToCompletion, task.Status);
                    Assert.Same(expectedContent, task.Result);
                }
                finally
                {
                    BindToStringProgram.TaskSource = null;
                }
            }
        }

        private static IServiceProvider CreateServiceProvider(IStorageAccount storageAccount, Type programType)
        {
            return new FakeServiceProvider
            {
                StorageAccountProvider = new FakeStorageAccountProvider
                {
                    StorageAccount = storageAccount
                },
                TypeLocator = new FakeTypeLocator(programType),
                ConnectionStringProvider = new NullConnectionStringProvider(),
                HostIdProvider = new FakeHostIdProvider(),
                QueueConfiguration = new FakeQueueConfiguration(),
                StorageCredentialsValidator = new FakeStorageCredentialsValidator()
            };
        }

        private class BindToStringProgram
        {
            public const string QueueName = "input";

            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void Run([QueueTrigger(QueueName)] string message)
            {
                TaskSource.TrySetResult(message);
            }
        }
    }
}
