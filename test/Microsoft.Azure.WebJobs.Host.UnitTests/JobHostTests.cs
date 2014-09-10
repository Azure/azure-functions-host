// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class JobHostTests
    {
        [Fact]
        public void StartAsync_WhenNotStarted_DoesNotThrow()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                // Act & Assert
                Assert.DoesNotThrow(() => host.StartAsync().GetAwaiter().GetResult());
            }
        }

        [Fact]
        public void StartAsync_WhenStarted_Throws()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => host.StartAsync(), "Start has already been called.");
            }
        }

        [Fact]
        public void StartAsync_WhenStopped_Throws()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();
                host.Stop();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => host.StartAsync(), "Start has already been called.");
            }
        }

        [Fact]
        public void StartAsync_WhenStarting_Throws()
        {
            // Arrange
            TaskCompletionSource<object> validateTaskSource = new TaskCompletionSource<object>();
            TestJobHostConfiguration configuration = CreateConfiguration(new LambdaStorageCredentialsValidator(
                    (i1, i2) => validateTaskSource.Task));

            using (JobHost host = new JobHost(configuration))
            {
                Task starting = host.StartAsync();
                Assert.False(starting.IsCompleted); // Guard

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => host.StartAsync(), "Start has already been called.");

                // Cleanup
                validateTaskSource.SetResult(null);
                starting.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void StartAsync_WhenStopping_Throws()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();

                // Replace (and cleanup) the exsiting runner to hook StopAsync.
                IListener oldListener = host.Listener;
                oldListener.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

                TaskCompletionSource<object> stopTaskSource = new TaskCompletionSource<object>();
                Mock<IListener> listenerMock = new Mock<IListener>(MockBehavior.Strict);
                listenerMock.Setup(r => r.StopAsync(It.IsAny<CancellationToken>())).Returns(stopTaskSource.Task);
                listenerMock.Setup(r => r.Dispose());
                host.Listener = listenerMock.Object;

                Task stopping = host.StopAsync();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => host.StartAsync(), "Start has already been called.");

                // Cleanup
                stopTaskSource.SetResult(null);
                stopping.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void StopAsync_WhenStarted_DoesNotThrow()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();

                // Act & Assert
                Assert.DoesNotThrow(() => host.StopAsync().GetAwaiter().GetResult());
            }
        }

        [Fact]
        public void StopAsync_WhenStopped_DoesNotThrow()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();
                host.Stop();

                // Act & Assert
                Assert.DoesNotThrow(() => host.StopAsync().GetAwaiter().GetResult());
            }
        }

        [Fact]
        public void StopAsync_WhenNotStarted_Throws()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => host.StopAsync(), "The host has not yet started.");
            }
        }

        [Fact]
        public void StopAsync_WhenStarting_Throws()
        {
            // Arrange
            TaskCompletionSource<object> validateTaskSource = new TaskCompletionSource<object>();
            TestJobHostConfiguration configuration = CreateConfiguration(new LambdaStorageCredentialsValidator(
                    (i1, i2) => validateTaskSource.Task));

            using (JobHost host = new JobHost(configuration))
            {
                Task starting = host.StartAsync();
                Assert.False(starting.IsCompleted); // Guard

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => host.StopAsync(), "The host has not yet started.");

                // Cleanup
                validateTaskSource.SetResult(null);
                starting.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void StopAsync_WhenWaiting_ReturnsIncompleteTask()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();

                // Replace (and cleanup) the existing listener to hook StopAsync.
                IListener oldListener = host.Listener;
                oldListener.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

                TaskCompletionSource<object> stopTaskSource = new TaskCompletionSource<object>();
                Mock<IListener> listenerMock = new Mock<IListener>(MockBehavior.Strict);
                listenerMock.Setup(r => r.StopAsync(It.IsAny<CancellationToken>())).Returns(stopTaskSource.Task);
                listenerMock.Setup(r => r.Dispose());
                host.Listener = listenerMock.Object;

                // Act
                Task stopping = host.StopAsync();

                // Assert
                Assert.False(stopping.IsCompleted);

                // Cleanup
                stopTaskSource.SetResult(null);
                stopping.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void StopAsync_WhenAlreadyStopping_ReturnsSameTask()
        {
            // Arrange
            using (JobHost host = new JobHost(CreateConfiguration()))
            {
                host.Start();

                // Replace (and cleanup) the existing listener to hook StopAsync.
                IListener oldRunner = host.Listener;
                oldRunner.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

                TaskCompletionSource<object> stopTaskSource = new TaskCompletionSource<object>();
                Mock<IListener> listenerMock = new Mock<IListener>(MockBehavior.Strict);
                listenerMock.Setup(r => r.StopAsync(It.IsAny<CancellationToken>())).Returns(stopTaskSource.Task);
                listenerMock.Setup(r => r.Dispose());
                host.Listener = listenerMock.Object;
                Task alreadyStopping = host.StopAsync();

                // Act
                Task stoppingAgain = host.StopAsync();

                // Assert
                Assert.Same(alreadyStopping, stoppingAgain);

                // Cleanup
                stopTaskSource.SetResult(null);
                alreadyStopping.GetAwaiter().GetResult();
                stoppingAgain.GetAwaiter().GetResult();
            }
        }

        [Fact]
        public void SimpleInvoke_WithDictionary()
        {
            var host = JobHostFactory.Create<ProgramSimple>(null);

            var x = "abc";
            ProgramSimple._value = null;
            host.Call("Test", new Dictionary<string, object> { { "value", x } });

            // Ensure test method was invoked properly.
            Assert.Equal(x, ProgramSimple._value);
        }

        [Fact]
        public void SimpleInvoke_WithObject()
        {
            var host = JobHostFactory.Create<ProgramSimple>(null);

            var x = "abc";
            ProgramSimple._value = null;
            host.Call("Test", new { value = x });

            // Ensure test method was invoked properly.
            Assert.Equal(x, ProgramSimple._value);
        }

        [Fact]
        public void CallAsyncWithCancellationToken_PassesCancellationTokenToMethod()
        {
            // Arrange
            ProgramWithCancellationToken.Cleanup();
            var host = JobHostFactory.Create<ProgramWithCancellationToken>(null);

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                ProgramWithCancellationToken.CancellationTokenSource = source;

                // Act
                host.CallAsync("BindCancellationToken", null, source.Token).GetAwaiter().GetResult();

                // Assert
                Assert.True(ProgramWithCancellationToken.IsCancellationRequested);
            }
        }

        [Fact]
        public void Call_WhenMethodThrows_PreservesStackTrace()
        {
            try
            {
                // Arrange
                InvalidOperationException expectedException = new InvalidOperationException();
                ExceptionDispatchInfo expectedExceptionInfo = CreateExceptionInfo(expectedException);
                string expectedStackTrace = expectedExceptionInfo.SourceException.StackTrace;
                ThrowingProgram.ExceptionInfo = expectedExceptionInfo;

                var host = JobHostFactory.Create<ThrowingProgram>(null);
                MethodInfo methodInfo = typeof(ThrowingProgram).GetMethod("Throw");

                // Act & Assert
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => host.Call(methodInfo));
                Assert.Same(exception, expectedException);
                Assert.NotNull(exception.StackTrace);
                Assert.True(exception.StackTrace.StartsWith(expectedStackTrace));
            }
            finally
            {
                ThrowingProgram.ExceptionInfo = null;
            }
        }

        private static TestJobHostConfiguration CreateConfiguration()
        {
            return CreateConfiguration(new NullStorageCredentialsValidator());
        }

        private static TestJobHostConfiguration CreateConfiguration(IStorageCredentialsValidator credentialsValidator)
        {
            return new TestJobHostConfiguration
            {
                TypeLocator = new SimpleTypeLocator(),
                StorageAccountProvider = new SimpleStorageAccountProvider
                {
                    // Nse null connection strings since unit tests shouldn't make wire requests.
                    StorageAccount = null,
                    DashboardAccount = null
                },
                StorageCredentialsValidator = credentialsValidator,
                ConnectionStringProvider = new NullConnectionStringProvider(),
            };
        }

        private static ExceptionDispatchInfo CreateExceptionInfo(Exception exception)
        {
            try
            {
                throw exception;
            }
            catch (Exception caught)
            {
                return ExceptionDispatchInfo.Capture(caught);
            }
        }

        private class ProgramSimple
        {
            public static string _value; // evidence of execution

            [NoAutomaticTrigger]
            public static void Test(string value)
            {
                _value = value;
            }
        }

        private class LambdaStorageCredentialsValidator : IStorageCredentialsValidator
        {
            private readonly Func<CloudStorageAccount, CancellationToken, Task> _validateCredentialsAsync;

            public LambdaStorageCredentialsValidator(
                Func<CloudStorageAccount, CancellationToken, Task> validateCredentialsAsync)
            {
                _validateCredentialsAsync = validateCredentialsAsync;
            }

            public Task ValidateCredentialsAsync(CloudStorageAccount account, CancellationToken cancellationToken)
            {
                return _validateCredentialsAsync.Invoke(account, cancellationToken);
            }
        }

        private class ProgramWithCancellationToken
        {
            public static CancellationTokenSource CancellationTokenSource { get; set; }

            public static bool IsCancellationRequested { get; private set; }

            public static void Cleanup()
            {
                CancellationTokenSource = null;
                IsCancellationRequested = false;
            }

            [NoAutomaticTrigger]
            public static void BindCancellationToken(CancellationToken cancellationToken)
            {
                CancellationTokenSource.Cancel();
                IsCancellationRequested = cancellationToken.IsCancellationRequested;
            }
        }

        private class ThrowingProgram
        {
            public static ExceptionDispatchInfo ExceptionInfo { get; set; }

            [NoAutomaticTrigger]
            public static void Throw()
            {
                ExceptionInfo.Throw();
            }
        }
    }
}
