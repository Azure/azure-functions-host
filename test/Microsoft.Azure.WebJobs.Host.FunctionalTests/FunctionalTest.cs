// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class FunctionalTest
    {
        public static void Call(IStorageAccount account, Type programType, MethodInfo method,
            IDictionary<string, object> arguments, params Type[] cloudBlobStreamBinderTypes)
        {
            // Arrange
            TaskCompletionSource<object> backgroundTaskSource = new TaskCompletionSource<object>();
            var serviceProvider = CreateConfigurationForManualCompletion<object>(account,
                programType, backgroundTaskSource, cloudBlobStreamBinderTypes: cloudBlobStreamBinderTypes);
            Task backgroundTask = backgroundTaskSource.Task;

            using (JobHost host = new JobHost(serviceProvider))
            {
                Task task = host.CallAsync(method, arguments);

                // Act
                bool completed = Task.WhenAny(task, backgroundTask).WaitUntilCompleted(3 * 1000);

                // Assert
                Assert.True(completed);

                // Give a nicer test failure message for faulted tasks.
                if (backgroundTask.Status == TaskStatus.Faulted)
                {
                    backgroundTask.GetAwaiter().GetResult();
                }

                // The background task should not complete.
                Assert.Equal(TaskStatus.WaitingForActivation, backgroundTask.Status);

                // Give a nicer test failure message for faulted tasks.
                if (task.Status == TaskStatus.Faulted)
                {
                    task.GetAwaiter().GetResult();
                }

                Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            }
        }

        // Stops running the host as soon as the program marks the task as completed.
        public static TResult Call<TResult>(IStorageAccount account, Type programType, MethodInfo method,
            IDictionary<string, object> arguments, Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            // Arrange
            TaskCompletionSource<TResult> taskSource = new TaskCompletionSource<TResult>();
            var serviceProvider = CreateConfigurationForManualCompletion<TResult>(account, programType,
                taskSource);
            Task<TResult> task = taskSource.Task;
            setTaskSource.Invoke(taskSource);

            try
            {
                using (JobHost host = new JobHost(serviceProvider))
                {
                    Task callTask = host.CallAsync(method, arguments);

                    // Act
                    bool completed = Task.WhenAll(task, callTask).WaitUntilCompleted(3 * 1000);

                    // Assert
                    Assert.True(completed);

                    // Give a nicer test failure message for faulted tasks.
                    if (task.Status == TaskStatus.Faulted)
                    {
                        task.GetAwaiter().GetResult();
                    }

                    if (callTask.Status == TaskStatus.Faulted)
                    {
                        callTask.GetAwaiter().GetResult();
                    }

                    Assert.Equal(TaskStatus.RanToCompletion, callTask.Status);

                    return task.Result;
                }
            }
            finally
            {
                setTaskSource.Invoke(null);
            }
        }

        // Stops running the host as soon as the program marks the task as completed.
        public static Exception CallFailure(IStorageAccount account, Type programType, MethodInfo method,
            IDictionary<string, object> arguments)
        {
            // Arrange
            TaskCompletionSource<object> backgroundTaskSource = new TaskCompletionSource<object>();
            var serviceProvider = CreateConfigurationForCallFailure(account, programType,
                backgroundTaskSource);
            Task backgroundTask = backgroundTaskSource.Task;

            using (JobHost host = new JobHost(serviceProvider))
            {
                Task callTask = host.CallAsync(method, arguments);

                // Act
                bool completed = Task.WhenAny(callTask, backgroundTask).WaitUntilCompleted(3 * 1000);

                // Assert
                Assert.True(completed);

                // Give a nicer test failure message for faulted tasks.
                if (backgroundTask.Status == TaskStatus.Faulted)
                {
                    backgroundTask.GetAwaiter().GetResult();
                }

                Assert.Equal(TaskStatus.Faulted, callTask.Status);
                return callTask.Exception.InnerException;
            }
        }

        public static JobHostConfiguration CreateConfigurationForCallFailure(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<object> taskSource)
        {
            return CreateConfiguration<object>(storageAccount, programType, new NullExtensionTypeLocator(),
                taskSource, new NullFunctionInstanceLogger());
        }

        private static JobHostConfiguration CreateConfigurationForInstanceFailure(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<Exception> taskSource)
        {
            return CreateConfiguration<Exception>(storageAccount, programType, new NullExtensionTypeLocator(),
                taskSource, new ExpectInstanceFailureTaskFunctionInstanceLogger(taskSource));
        }

        public static JobHostConfiguration CreateConfigurationForInstanceSuccess(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<object> taskSource, IExtensionRegistry extensions = null)
        {
            return CreateConfiguration<object>(storageAccount, programType, new NullExtensionTypeLocator(),
                taskSource, new ExpectInstanceSuccessTaskFunctionInstanceLogger(taskSource), extensions);
        }

        public static JobHostConfiguration CreateConfigurationForManualCompletion<TResult>(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<TResult> taskSource, IExtensionRegistry extensions = null, params Type[] cloudBlobStreamBinderTypes)
        {
            IEnumerable<string> ignoreFailureFunctionIds = null;
            return CreateConfigurationForManualCompletion<TResult>(storageAccount, programType, taskSource,
                ignoreFailureFunctionIds, extensions, cloudBlobStreamBinderTypes);
        }

        private static JobHostConfiguration CreateConfigurationForManualCompletion<TResult>(
            IStorageAccount storageAccount, Type programType, TaskCompletionSource<TResult> taskSource,
            IEnumerable<string> ignoreFailureFunctions, IExtensionRegistry extensions = null, params Type[] cloudBlobStreamBinderTypes)
        {
            return CreateConfigurationForManualCompletion<TResult>(storageAccount, programType,
                DefaultJobActivator.Instance, taskSource, ignoreFailureFunctions, extensions, cloudBlobStreamBinderTypes);
        }

        private static JobHostConfiguration CreateConfigurationForManualCompletion<TResult>(
            IStorageAccount storageAccount, Type programType, IJobActivator activator,
            TaskCompletionSource<TResult> taskSource, IEnumerable<string> ignoreFailureFunctions, IExtensionRegistry extensions = null,
            params Type[] cloudBlobStreamBinderTypes)
        {
            IExtensionTypeLocator extensionTypeLocator;

            if (cloudBlobStreamBinderTypes == null || cloudBlobStreamBinderTypes.Length == 0)
            {
                extensionTypeLocator = new NullExtensionTypeLocator();
            }
            else
            {
                extensionTypeLocator = new FakeExtensionTypeLocator(cloudBlobStreamBinderTypes);
            }

            return CreateConfiguration<TResult>(storageAccount, programType, extensionTypeLocator, activator, taskSource,
                new ExpectManualCompletionFunctionInstanceLogger<TResult>(taskSource, ignoreFailureFunctions), extensions);
        }

        private static JobHostConfiguration CreateConfiguration<TResult>(IStorageAccount storageAccount, Type programType,
            IExtensionTypeLocator extensionTypeLocator, TaskCompletionSource<TResult> taskSource,
            IFunctionInstanceLogger functionInstanceLogger, IExtensionRegistry extensions = null)
        {
            return CreateConfiguration<TResult>(storageAccount, programType, extensionTypeLocator,
                DefaultJobActivator.Instance, taskSource, functionInstanceLogger, extensions);
        }

        private static JobHostConfiguration CreateConfiguration<TResult>(IStorageAccount storageAccount, Type programType,
            IExtensionTypeLocator extensionTypeLocator, IJobActivator activator,
            TaskCompletionSource<TResult> taskSource, IFunctionInstanceLogger functionInstanceLogger, IExtensionRegistry extensions = null)
        {
            IStorageAccountProvider storageAccountProvider = new FakeStorageAccountProvider
            {
                StorageAccount = storageAccount
            };
            IHostIdProvider hostIdProvider = new FakeHostIdProvider();
            IWebJobsExceptionHandler exceptionHandler = new TaskBackgroundExceptionHandler<TResult>(taskSource);

            return TestHelpers.NewConfig(
                programType,
                new FakeQueueConfiguration(storageAccountProvider),
                storageAccountProvider,
                extensionTypeLocator,
                activator,
                extensions,
                exceptionHandler,
                new NullFunctionInstanceLoggerProvider(functionInstanceLogger),
                new NullHostInstanceLoggerProvider(),
                new NullFunctionOutputLoggerProvider()
                );
        }

        // Stops running the host as soon as the first function logs completion.
        public static void RunTrigger(IStorageAccount account, Type programType, IExtensionRegistry extensions = null)
        {
            // Arrange
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            var serviceProvider = CreateConfigurationForInstanceSuccess(account, programType, taskSource, extensions);

            // Act & Assert
            RunTrigger<object>(serviceProvider, taskSource.Task);
        }

        // Stops running the host as soon as the program marks the task as completed.
        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return RunTrigger<TResult>(account, programType, setTaskSource, DefaultJobActivator.Instance,
                ignoreFailureFunctions: null);
        }

        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            IJobActivator activator, Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return RunTrigger<TResult>(account, programType, setTaskSource, activator, ignoreFailureFunctions: null);
        }

        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, IEnumerable<string> ignoreFailureFunctions)
        {
            return RunTrigger<TResult>(account, programType, setTaskSource, DefaultJobActivator.Instance, ignoreFailureFunctions);
        }

        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, IJobActivator activator,
            IEnumerable<string> ignoreFailureFunctions)
        {
            // Arrange
            TaskCompletionSource<TResult> taskSource = new TaskCompletionSource<TResult>();
            var serviceProvider = CreateConfigurationForManualCompletion<TResult>(account, programType,
                activator, taskSource, ignoreFailureFunctions);
            Task<TResult> task = taskSource.Task;
            setTaskSource.Invoke(taskSource);

            try
            {
                // Act & Assert
                return RunTrigger<TResult>(serviceProvider, task);
            }
            finally
            {
                setTaskSource.Invoke(null);
            }
        }

        public static TResult RunTrigger<TResult>(JobHostConfiguration config, Task<TResult> task)
        {
            // Arrange
            bool completed;

            using (JobHost host = new JobHost(config))
            {
                host.Start();

                // Act
                if (Debugger.IsAttached)
                {
                    task.WaitUntilCompleted();
                    completed = true;
                }
                else
                {
                    completed = task.WaitUntilCompleted(25 * 1000);
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
        }

        // Stops running the host as soon as the first function logs completion.
        public static Exception RunTriggerFailure(IStorageAccount account, Type programType)
        {
            // Arrange
            TaskCompletionSource<Exception> taskSource = new TaskCompletionSource<Exception>();
            var serviceProvider = CreateConfigurationForInstanceFailure(account, programType,
                taskSource);
            // The task for failed function invocation (should complete successfully with a non-null exception).
            Task<Exception> task = taskSource.Task;

            using (JobHost host = new JobHost(serviceProvider))
            {
                host.Start();

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
                Exception exception = task.Result;
                Assert.IsType<FunctionInvocationException>(exception);
                return exception.InnerException;
            }
        }

        // Stops running the host as soon as the program marks the task as completed.
        public static Exception RunTriggerFailure<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            // Arrange
            TaskCompletionSource<Exception> failureTaskSource = new TaskCompletionSource<Exception>();
            var serviceProvider = CreateConfigurationForInstanceFailure(account, programType, failureTaskSource);
            TaskCompletionSource<TResult> successTaskSource = new TaskCompletionSource<TResult>();
            // The task for failed function invocation (should complete successfully with an exception).
            Task<Exception> failureTask = failureTaskSource.Task;
            // The task for successful function invocation (should not complete).
            Task<TResult> successTask = successTaskSource.Task;
            setTaskSource.Invoke(successTaskSource);

            try
            {
                using (JobHost host = new JobHost(serviceProvider))
                {
                    host.Start();

                    // Act
                    bool completed = Task.WhenAny(failureTask, successTask).WaitUntilCompleted(3 * 1000);

                    // Assert
                    Assert.True(completed);

                    // Give a nicer test failure message for faulted tasks.
                    if (successTask.Status == TaskStatus.Faulted)
                    {
                        successTask.GetAwaiter().GetResult();
                    }

                    // The function should not be invoked.
                    Assert.Equal(TaskStatus.WaitingForActivation, successTask.Status);

                    // Give a nicer test failure message for faulted tasks.
                    if (failureTask.Status == TaskStatus.Faulted)
                    {
                        successTask.GetAwaiter().GetResult();
                    }

                    Assert.Equal(TaskStatus.RanToCompletion, failureTask.Status);
                    Exception exception = failureTask.Result;
                    Assert.IsType<FunctionInvocationException>(exception);
                    return exception.InnerException;
                }
            }
            finally
            {
                setTaskSource.Invoke(null);
            }
        }
    }
}
