// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class HostStopTests
    {
        private const string QueueName = "input";

        [Fact]
        public void Stop_TriggersCancellationToken()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue queue = CreateQueue(account, QueueName);
            IStorageQueueMessage message = queue.CreateMessage("ignore");
            queue.AddMessage(message);

            // Act
            Task stopTask = null;
            bool result = RunTrigger<bool>(account, typeof(CallbackCancellationTokenProgram),
                (s) => CallbackCancellationTokenProgram.TaskSource = s,
                (t) => CallbackCancellationTokenProgram.Start = t, (h) => stopTask = h.StopAsync());

            // Assert
            Assert.True(result);
            stopTask.WaitUntilCompleted(3 * 1000);
            Assert.Equal(TaskStatus.RanToCompletion, stopTask.Status);
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

        // Stops running the host as soon as the program marks the task as completed.
        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, Action<Task> setStartTask, Action<JobHost> callback)
        {
            TaskCompletionSource<object> startTaskSource = new TaskCompletionSource<object>();
            setStartTask.Invoke(startTaskSource.Task);

            // Arrange
            try
            {
                TaskCompletionSource<TResult> taskSource = new TaskCompletionSource<TResult>();
                var serviceProvider = FunctionalTest.CreateConfigurationForManualCompletion<TResult>(
                    account, programType, taskSource);
                Task<TResult> task = taskSource.Task;
                setTaskSource.Invoke(taskSource);

                try
                {
                    // Arrange
                    JobHost host = new JobHost(serviceProvider);
                    host.Start();
                    callback.Invoke(host);
                    startTaskSource.TrySetResult(null);

                    // Act
                    bool completed = task.WaitUntilCompleted(3 * 1000);

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
                finally
                {
                    setTaskSource.Invoke(null);
                }
            }
            finally
            {
                setStartTask.Invoke(null);
            }
        }

        private class CallbackCancellationTokenProgram
        {
            public static Task Start { get; set; }
            public static TaskCompletionSource<bool> TaskSource { get; set; }

            public static void CallbackCancellationToken([QueueTrigger(QueueName)] string ignore,
                CancellationToken cancellationToken)
            {
                bool started = Start.WaitUntilCompleted(3 * 1000);
                Assert.True(started); // Guard
                TaskSource.TrySetResult(cancellationToken.IsCancellationRequested);
            }
        }
    }
}
