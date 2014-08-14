// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.Jobs.Host.EndToEndTests
{
    public class AsyncCancellationEndToEndTests : IDisposable
    {
        private const string QueueName = "asynccancele2e%rnd%";
        private const int DefaultTimeout = 5 * 1000;

        private static Action _invokeInFunction;
        private static bool _invokeInFunctionInvoked;
        private static EventWaitHandle _functionStarted;
        private static EventWaitHandle _functionCompleted;
        private static bool _tokenCancelled;

        private readonly CloudStorageAccount _storageAccount;
        private readonly RandomNameResolver _resolver;
        private readonly JobHostConfiguration _hostConfiguration;

        public AsyncCancellationEndToEndTests()
        {
            _resolver = new RandomNameResolver();

            _hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = _resolver,
                TypeLocator = new SimpleTypeLocator(typeof(AsyncCancellationEndToEndTests))
            };

            _storageAccount = CloudStorageAccount.Parse(_hostConfiguration.StorageConnectionString);

            _invokeInFunction = () => { };
            _tokenCancelled = false;
            _functionStarted = new ManualResetEvent(initialState: false);
            _functionCompleted = new ManualResetEvent(initialState: false);
        }

        public void Dispose()
        {
            _functionStarted.Dispose();
            _functionCompleted.Dispose();

            if (_storageAccount != null)
            {
                CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
                CloudQueue queue = queueClient.GetQueueReference(_resolver.ResolveInString(QueueName));
                queue.DeleteIfExists();
            }
        }


        [NoAutomaticTrigger]
        public static void InfiniteRunningFunctionUnlessCancelledManual(
            CancellationToken token)
        {
            FunctionBody(token);
        }

        [NoAutomaticTrigger]
        public static void WriteQueueMessage(
            [Queue(QueueName)] out string message)
        {
            message = "test";
        }

        public static void InfiniteRunningFunctionUnlessCancelledTriggered(
            [QueueTrigger(QueueName)] string message,
            CancellationToken token)
        {
            FunctionBody(token);
        }

        private static void FunctionBody(CancellationToken token)
        {
            _functionStarted.Set();

            // If the token is cancelled here, something is not right
            if (!token.IsCancellationRequested)
            {
                _invokeInFunction();
                _invokeInFunctionInvoked = true;

                if (token.WaitHandle.WaitOne(DefaultTimeout))
                {
                    _tokenCancelled = token.IsCancellationRequested;
                }
            }

            _functionCompleted.Set();
        }

        [Fact]
        public void WebJobsShutdown_WhenUsingHostCall_TriggersCancellationToken()
        {
            using (WebJobsShutdownContext shutdownContext = new WebJobsShutdownContext())
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                _invokeInFunction = () => { shutdownContext.NotifyShutdown(); };

                Task callTask = InvokeNoAutomaticTriggerFunction(host);

                EvaluateNoAutomaticTriggerCancellation(callTask, expectedCancellation: true);
            }
        }

        [Fact]
        public void WebJobsShutdown_WhenUsingTriggeredFunction_TriggersCancellationToken()
        {
            using (WebJobsShutdownContext shutdownContext = new WebJobsShutdownContext())
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                _invokeInFunction = () => { shutdownContext.NotifyShutdown(); };

                PrepareHostForTrigger(host, startHost: true);

                EvaluateTriggeredCancellation(expectedCancellation: true);
            }
        }

        [Fact]
        public void Stop_WhenUsingHostCall_DoesNotTriggerCancellationToken()
        {
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                host.Start();

                Task callTask = InvokeNoAutomaticTriggerFunction(host);

                host.Stop();

                EvaluateNoAutomaticTriggerCancellation(callTask, expectedCancellation: false);
            }
        }

        [Fact]
        public void Stop_WhenUsingTriggeredFunction_TriggersCancellationToken()
        {
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                PrepareHostForTrigger(host, startHost: true);

                host.Stop();

                EvaluateTriggeredCancellation(expectedCancellation: true);
            }
        }

        [Fact]
        public void Dispose_WhenUsingHostCall_TriggersCancellationToken()
        {
            Task callTask;

            using (JobHost host = new JobHost(_hostConfiguration))
            {
                callTask = InvokeNoAutomaticTriggerFunction(host);
            }

            EvaluateNoAutomaticTriggerCancellation(callTask, expectedCancellation: true);
        }

        [Fact]
        public void Dispose_WhenUsingTriggeredFunction_TriggersCancellationToken()
        {
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                PrepareHostForTrigger(host, startHost: true);
            }

            EvaluateTriggeredCancellation(expectedCancellation: true);
        }

        [Fact]
        public void CallCancellationToken_WhenUsingHostCall_TriggersCancellationToken()
        {
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                _invokeInFunction = () => { tokenSource.Cancel(); };

                Task callTask = InvokeNoAutomaticTriggerFunction(host, tokenSource.Token);

                EvaluateNoAutomaticTriggerCancellation(callTask, expectedCancellation: true);
            }
        }

        [Fact]
        public void CallCancellationToken_WhenUsingTriggeredFunction_DoesNotTriggerCancellationToken()
        {
            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                _invokeInFunction = () => { tokenSource.Cancel(); };

                PrepareHostForTrigger(host, startHost: false);
                Assert.True(host.StartAsync(tokenSource.Token).WaitUntilCompleted(DefaultTimeout));

                EvaluateTriggeredCancellation(expectedCancellation: false);
            }
        }

        private void PrepareHostForTrigger(JobHost host, bool startHost)
        {
            host.Call(typeof(AsyncCancellationEndToEndTests).GetMethod("WriteQueueMessage"));

            if (startHost)
            {
                host.Start();
                Assert.True(_functionStarted.WaitOne(DefaultTimeout));
            }
        }

        private Task InvokeNoAutomaticTriggerFunction(JobHost host)
        {
            return InvokeNoAutomaticTriggerFunction(host, CancellationToken.None);
        }

        private Task InvokeNoAutomaticTriggerFunction(JobHost host, CancellationToken token)
        {
            Task callTask = host.CallAsync(
                typeof(AsyncCancellationEndToEndTests).GetMethod("InfiniteRunningFunctionUnlessCancelledManual"),
                token);
            Assert.True(_functionStarted.WaitOne(DefaultTimeout));

            return callTask;
        }

        private void EvaluateTriggeredCancellation(bool expectedCancellation)
        {
            // Wait for the function to complete
            Assert.True(_functionCompleted.WaitOne(2 * DefaultTimeout));
            Assert.Equal(expectedCancellation, _tokenCancelled);
            Assert.True(_invokeInFunctionInvoked);
        }

        private void EvaluateNoAutomaticTriggerCancellation(Task task, bool expectedCancellation)
        {
            bool taskCompleted = task.WaitUntilCompleted(2 * DefaultTimeout);
            bool taskCompletedBeforeFunction = !_functionCompleted.WaitOne(0);

            Assert.True(taskCompleted);
            Assert.False(taskCompletedBeforeFunction);
            Assert.Equal(expectedCancellation, task.IsCanceled);
            Assert.Equal(expectedCancellation, _tokenCancelled);
            Assert.True(_invokeInFunctionInvoked);
        }
    }
}
