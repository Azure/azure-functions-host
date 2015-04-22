// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;
using Moq;
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
            CloudQueueMessage result = RunTrigger<CloudQueueMessage>(account, typeof(InstanceProgram),
                (s) => InstanceProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        private class InstanceProgram
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
            CloudQueueMessage result = RunTrigger<CloudQueueMessage>(account, typeof(InstanceAsyncProgram),
                (s) => InstanceAsyncProgram.TaskSource = s);

            // Assert
            Assert.Same(expectedMessage, result);
        }

        private class InstanceAsyncProgram
        {
            public static TaskCompletionSource<CloudQueueMessage> TaskSource { get; set; }

            public Task RunAsync([QueueTrigger(QueueName)] CloudQueueMessage message)
            {
                TaskSource.TrySetResult(message);
                return Task.FromResult(0);
            }
        }

        [Fact]
        public void Trigger_IfClassIsDisposable_Disposes()
        {
            // Arrange
            CloudQueueMessage expectedMessage = new CloudQueueMessage("ignore");
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(new FakeStorageQueueMessage(expectedMessage));

            // Act & Assert
            RunTrigger<object>(account, typeof(DisposeInstanceProgram),
                (s) => DisposeInstanceProgram.TaskSource = s);
        }

        private sealed class DisposeInstanceProgram : IDisposable
        {
            public static TaskCompletionSource<object> TaskSource { get; set; }

            public void Run([QueueTrigger(QueueName)] CloudQueueMessage message)
            {
            }

            public void Dispose()
            {
                TaskSource.TrySetResult(null);
            }
        }

        [Fact]
        public void Trigger_IfClassConstructorHasDependencies_CanUseCustomJobActivator()
        {
            // Arrange
            const string expectedResult = "abc";

            Mock<IFactory<string>> resultFactoryMock = new Mock<IFactory<string>>(MockBehavior.Strict);
            resultFactoryMock.Setup(f => f.Create()).Returns(expectedResult);
            IFactory<string> resultFactory = resultFactoryMock.Object;

            Mock<IJobActivator> activatorMock = new Mock<IJobActivator>(MockBehavior.Strict);
            activatorMock.Setup(a => a.CreateInstance<InstanceCustomActivatorProgram>())
                         .Returns(() => new InstanceCustomActivatorProgram(resultFactory));
            IJobActivator activator = activatorMock.Object;

            CloudQueueMessage message = new CloudQueueMessage("ignore");
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            queue.AddMessage(new FakeStorageQueueMessage(message));

            // Act
            string result = RunTrigger<string>(account, typeof(InstanceCustomActivatorProgram),
                (s) => InstanceCustomActivatorProgram.TaskSource = s, activator);

            // Assert
            Assert.Same(expectedResult, result);
        }

        private class InstanceCustomActivatorProgram
        {
            private readonly IFactory<string> _resultFactory;

            public InstanceCustomActivatorProgram(IFactory<string> resultFactory)
            {
                if (resultFactory == null)
                {
                    throw new ArgumentNullException("resultFactory");
                }

                _resultFactory = resultFactory;
            }

            public static TaskCompletionSource<string> TaskSource { get; set; }

            public void Run([QueueTrigger(QueueName)] CloudQueueMessage ignore)
            {
                string result = _resultFactory.Create();
                TaskSource.TrySetResult(result);
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

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, IJobActivator activator)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, activator, setTaskSource);
        }
    }
}
