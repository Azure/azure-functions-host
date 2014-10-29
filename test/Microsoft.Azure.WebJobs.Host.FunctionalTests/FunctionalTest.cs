// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            IServiceProvider serviceProvider = CreateServiceProviderForManualCompletion<object>(account,
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
            IServiceProvider serviceProvider = CreateServiceProviderForManualCompletion<TResult>(account, programType,
                taskSource);
            Task<TResult> task = taskSource.Task;
            setTaskSource.Invoke(taskSource);

            try
            {
                using (JobHost host = new JobHost(serviceProvider))
                {
                    Task callTask = host.CallAsync(method, arguments);

                    // Act
                    bool completed = Task.WhenAny(task, callTask).WaitUntilCompleted(3 * 1000);

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

        private static IServiceProvider CreateServiceProviderForInstanceFailure(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<Exception> taskSource)
        {
            return CreateServiceProvider<Exception>(storageAccount, programType, new NullExtensionTypeLocator(),
                taskSource, new ExpectInstanceFailureTaskFunctionInstanceLogger(taskSource));
        }

        public static IServiceProvider CreateServiceProviderForInstanceSuccess(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<object> taskSource)
        {
            return CreateServiceProvider<object>(storageAccount, programType, new NullExtensionTypeLocator(),
                taskSource, new ExpectInstanceSuccessTaskFunctionInstanceLogger(taskSource));
        }

        public static IServiceProvider CreateServiceProviderForManualCompletion<TResult>(IStorageAccount storageAccount,
            Type programType, TaskCompletionSource<TResult> taskSource, params Type[] cloudBlobStreamBinderTypes)
        {
            IEnumerable<string> ignoreFailureFunctionIds = null;
            return CreateServiceProviderForManualCompletion<TResult>(storageAccount, programType, taskSource,
                ignoreFailureFunctionIds, cloudBlobStreamBinderTypes);
        }

        private static IServiceProvider CreateServiceProviderForManualCompletion<TResult>(
            IStorageAccount storageAccount, Type programType, TaskCompletionSource<TResult> taskSource,
            IEnumerable<string> ignoreFailureFunctions, params Type[] cloudBlobStreamBinderTypes)
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

            return CreateServiceProvider<TResult>(storageAccount, programType, extensionTypeLocator, taskSource,
                new ExpectManualCompletionFunctionInstanceLogger<TResult>(taskSource, ignoreFailureFunctions));
        }

        private static IServiceProvider CreateServiceProvider<TResult>(IStorageAccount storageAccount, Type programType,
            IExtensionTypeLocator extensionTypeLocator, TaskCompletionSource<TResult> taskSource,
            IFunctionInstanceLogger functionInstanceLogger)
        {
            IStorageAccountProvider storageAccountProvider = new FakeStorageAccountProvider
            {
                StorageAccount = storageAccount
            };
            IServiceBusAccountProvider serviceBusAccountProvider = new NullServiceBusAccountProvider();
            IHostIdProvider hostIdProvider = new FakeHostIdProvider();
            INameResolver nameResolver = null;
            IQueueConfiguration queueConfiguration = new FakeQueueConfiguration();
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher =
                new TaskBackgroundExceptionDispatcher<TResult>(taskSource);
            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor =
                new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor =
                new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, serviceBusAccountProvider, extensionTypeLocator, hostIdProvider,
                queueConfiguration, backgroundExceptionDispatcher, messageEnqueuedWatcherAccessor,
                blobWrittenWatcherAccessor, sharedContextProvider, TextWriter.Null);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(nameResolver, storageAccountProvider,
                serviceBusAccountProvider, extensionTypeLocator, messageEnqueuedWatcherAccessor,
                blobWrittenWatcherAccessor);

            return new FakeServiceProvider
            {
                FunctionIndexProvider = new FunctionIndexProvider(new FakeTypeLocator(programType),
                    triggerBindingProvider, bindingProvider),
                StorageAccountProvider = storageAccountProvider,
                ServiceBusAccountProvider = serviceBusAccountProvider,
                BackgroundExceptionDispatcher = backgroundExceptionDispatcher,
                BindingProvider = bindingProvider,
                ConsoleProvider = new NullConsoleProvider(),
                HostInstanceLoggerProvider = new NullHostInstanceLoggerProvider(),
                FunctionInstanceLoggerProvider = new FakeFunctionInstanceLoggerProvider(functionInstanceLogger),
                FunctionOutputLoggerProvider = new NullFunctionOutputLoggerProvider(),
                HostIdProvider = hostIdProvider,
                QueueConfiguration = new FakeQueueConfiguration()
            };
        }

        // Stops running the host as soon as the first function logs completion.
        public static void RunTrigger(IStorageAccount account, Type programType)
        {
            // Arrange
            TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
            IServiceProvider serviceProvider = CreateServiceProviderForInstanceSuccess(account, programType,
                taskSource);

            // Act & Assert
            RunTrigger<object>(serviceProvider, taskSource.Task);
        }

        // Stops running the host as soon as the program marks the task as completed.
        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return RunTrigger<TResult>(account, programType, setTaskSource, ignoreFailureFunctions: null);
        }
        
        public static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, IEnumerable<string> ignoreFailureFunctions)
        {
            // Arrange
            TaskCompletionSource<TResult> taskSource = new TaskCompletionSource<TResult>();
            IServiceProvider serviceProvider = CreateServiceProviderForManualCompletion<TResult>(account, programType,
                taskSource, ignoreFailureFunctions);
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

        public static TResult RunTrigger<TResult>(IServiceProvider serviceProvider, Task<TResult> task)
        {
            // Arrange
            bool completed;

            using (JobHost host = new JobHost(serviceProvider))
            {
                host.Start();

                // Act
                completed = task.WaitUntilCompleted(10 * 1000);

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
            IServiceProvider serviceProvider = CreateServiceProviderForInstanceFailure(account, programType,
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
                return task.Result;
            }
        }

        // Stops running the host as soon as the program marks the task as completed.
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
                    return failureTask.Result;
                }
            }
            finally
            {
                setTaskSource.Invoke(null);
            }
        }
    }
}
