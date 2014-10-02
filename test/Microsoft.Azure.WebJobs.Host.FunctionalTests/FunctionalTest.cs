// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class FunctionalTest
    {
        public static IServiceProvider CreateServiceProvider<TResult>(IStorageAccount storageAccount, Type programType,
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
                StorageCredentialsValidator = new NullStorageCredentialsValidator()
            };
        }

        private static IServiceProvider CreateServiceProviderForInstanceFailure(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<Exception> taskSource)
        {
            return new FakeServiceProvider
            {
                StorageAccountProvider = new FakeStorageAccountProvider
                {
                    StorageAccount = storageAccount
                },
                TypeLocator = new FakeTypeLocator(programType),
                BackgroundExceptionDispatcher = new TaskBackgroundExceptionDispatcher<Exception>(taskSource),
                HostInstanceLogger = new NullHostInstanceLogger(),
                FunctionInstanceLogger = new TaskFailedFunctionInstanceLogger(taskSource),
                ConnectionStringProvider = new NullConnectionStringProvider(),
                HostIdProvider = new FakeHostIdProvider(),
                QueueConfiguration = new FakeQueueConfiguration(),
                StorageCredentialsValidator = new NullStorageCredentialsValidator()
            };
        }

        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
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

        public static Exception RunTriggerFailure<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            // Arrange
            TaskCompletionSource<Exception> failureTaskSource = new TaskCompletionSource<Exception>();
            IServiceProvider serviceProvider = CreateServiceProviderForInstanceFailure(account, programType,
                failureTaskSource);
            TaskCompletionSource<TResult> successTaskSource = new TaskCompletionSource<TResult>();
            // The task for failed function invocation (should complete successfully with an exception).
            Task<Exception> failureTask = failureTaskSource.Task;
            // The task for successful function invocation (should not complete).
            Task<TResult> successTask = successTaskSource.Task;
            setTaskSource.Invoke(successTaskSource);
            bool completed;

            using (JobHost host = new JobHost(serviceProvider))
            {
                try
                {
                    host.Start();

                    // Act
                    completed = Task.WhenAny(failureTask, successTask).WaitUntilCompleted(30 * 1000);
                }
                finally
                {
                    setTaskSource.Invoke(null);
                }
            }

            // Assert
            Assert.True(completed);

            // The function should not be invoked.
            Assert.Equal(TaskStatus.WaitingForActivation, successTask.Status);

            // Give a nicer test failure message for faulted tasks.
            if (failureTask.Status == TaskStatus.Faulted)
            {
                successTask.GetAwaiter().GetResult();
            }

            Assert.Equal(TaskStatus.RanToCompletion, failureTask.Status);
            return failureTask.Result;
        }
    }
}
