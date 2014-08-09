// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.Azure.Jobs.Host.Timers;
using Moq;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Timers
{
    public class IntervalSeparationTimerTests
    {
        [Fact]
        public void Constructor_IfCommandIsNull_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = null;
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher =
                new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict).Object;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(command, backgroundExceptionDispatcher),
                "command");
        }

        [Fact]
        public void Constructor_IfBackgroundExceptionDispatcherIsNull_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(command, backgroundExceptionDispatcher),
                "backgroundExceptionDispatcher");
        }

        [Fact]
        public void Start_AfterSeparationInterval_Executes()
        {
            // Arrange
            TimeSpan interval = TimeSpan.FromMilliseconds(1);

            using (EventWaitHandle executedWaitHandle = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() => executedWaitHandle.Set(), interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    // Act
                    product.Start();
                    bool executed = executedWaitHandle.WaitOne(1000);

                    // Assert
                    Assert.True(executed);
                }
            }
        }

        [Fact]
        public void Start_AfterSeparationInternalChanges_WaitsForNewInterval()
        {
            // Arrange
            bool executedOnce = false;
            bool executedTwice = false;
            TimeSpan initialInterval = TimeSpan.Zero;
            TimeSpan subsequentInterval = TimeSpan.FromMilliseconds(5);
            Stopwatch stopwatch = new Stopwatch();

            using (EventWaitHandle waitForSecondExecution = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    if (executedTwice)
                    {
                        return;
                    }

                    if (!executedOnce)
                    {
                        stopwatch.Start();
                        executedOnce = true;
                    }
                    else
                    {
                        stopwatch.Stop();
                        executedTwice = true;
                        waitForSecondExecution.Set();
                    }
                },
                () => executedOnce ? subsequentInterval : initialInterval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    // Act
                    product.Start();
                    waitForSecondExecution.WaitOne();

                    // Assert
                    Assert.True(stopwatch.ElapsedMilliseconds >= subsequentInterval.TotalMilliseconds,
                        String.Format("{0} >= {1}", stopwatch.ElapsedMilliseconds,
                        subsequentInterval.TotalMilliseconds));
                }
            }
        }

        [Fact]
        public void Start_IfCommandExecuteAsyncsThrows_CallsBackgroundExceptionDispatcher()
        {
            // Arrange
            Exception expectedException = new Exception();

            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle exceptionDispatchedWaitHandle = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    throw expectedException;
                }, interval);

                Mock<IBackgroundExceptionDispatcher> backgroundExceptionDispatcherMock =
                    new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
                ExceptionDispatchInfo exceptionInfo = null;
                backgroundExceptionDispatcherMock
                    .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                    .Callback<ExceptionDispatchInfo>((i) =>
                    {
                        exceptionInfo = i;
                        exceptionDispatchedWaitHandle.Set();
                    });
                IBackgroundExceptionDispatcher backgroundExceptionDispatcher = backgroundExceptionDispatcherMock.Object;

                using (IntervalSeparationTimer product = CreateProductUnderTest(command, backgroundExceptionDispatcher))
                {
                    // Act
                    product.Start();
                    bool executed = exceptionDispatchedWaitHandle.WaitOne(1000);

                    // Assert
                    Assert.True(executed);
                    Assert.NotNull(exceptionInfo);
                    Assert.Same(expectedException, exceptionInfo.SourceException);
                }
            }
        }

        [Fact]
        public void Start_IfCommandExecuteAsyncsReturnsFaultedTask_CallsBackgroundExceptionDispatcher()
        {
            // Arrange
            Exception expectedException = new Exception();
            bool executedOnce = false;

            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle executedTwiceWaitHandle = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        executedTwiceWaitHandle.Set();
                        return Task.FromResult(0);
                    }

                    executedOnce = true;
                    TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
                    taskSource.SetException(expectedException);
                    return taskSource.Task;
                }, interval);

                Mock<IBackgroundExceptionDispatcher> backgroundExceptionDispatcherMock =
                    new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
                ExceptionDispatchInfo exceptionInfo = null;
                backgroundExceptionDispatcherMock
                    .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                    .Callback<ExceptionDispatchInfo>((i) => exceptionInfo = i);
                IBackgroundExceptionDispatcher backgroundExceptionDispatcher = backgroundExceptionDispatcherMock.Object;

                using (IntervalSeparationTimer product = CreateProductUnderTest(command, backgroundExceptionDispatcher))
                {
                    // Act
                    product.Start();
                    bool executed = executedTwiceWaitHandle.WaitOne(1000);

                    // Assert
                    Assert.NotNull(exceptionInfo);
                    Assert.Same(expectedException, exceptionInfo.SourceException);
                }
            }
        }

        [Fact]
        public void Start_IfCommandExecuteAsyncsReturnsCanceledTask_DoesNotCallBackgroundExceptionDispatcher()
        {
            // Arrange
            bool executedOnce = false;

            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle executedTwiceWaitHandle = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        executedTwiceWaitHandle.Set();
                        return Task.FromResult(0);
                    }

                    executedOnce = true;
                    TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
                    taskSource.SetCanceled();
                    return taskSource.Task;
                }, interval);

                Mock<IBackgroundExceptionDispatcher> backgroundExceptionDispatcherMock =
                    new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
                int backgroundExceptionCalls = 0;
                backgroundExceptionDispatcherMock
                    .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                    .Callback(() => backgroundExceptionCalls++);
                IBackgroundExceptionDispatcher backgroundExceptionDispatcher = backgroundExceptionDispatcherMock.Object;

                using (IntervalSeparationTimer product = CreateProductUnderTest(command, backgroundExceptionDispatcher))
                {
                    // Act
                    product.Start();
                    bool executed = executedTwiceWaitHandle.WaitOne(1000);

                    // Assert
                    Assert.Equal(0, backgroundExceptionCalls);
                }
            }
        }

        [Fact]
        public void Start_IfDisposed_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Start());
        }

        [Fact]
        public void Start_IfStarted_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateStubCommand(TimeSpan.FromDays(1));

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                product.Start();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.Start(),
                    "The timer has already been started; it cannot be restarted.");
            }
        }

        [Fact]
        public void Stop_TriggersCommandCancellationToken()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle executeFinished = new ManualResetEvent(initialState: false))
            {
                bool cancellationTokenSignalled = false;

                IIntervalSeparationCommand command = CreateCommand((cancellationToken) =>
                {
                    executeStarted.Set();
                    cancellationTokenSignalled = cancellationToken.WaitHandle.WaitOne(1000);
                    executeFinished.Set();
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    executeStarted.WaitOne();

                    // Act
                    product.Stop();

                    // Assert
                    executeFinished.WaitOne();
                    Assert.True(cancellationTokenSignalled);
                }
            }
        }

        [Fact]
        public void Stop_WaitsForExecuteToFinish()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;
            bool executeFinished = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    executeStarted.Set();
                    stopExecuteInFiveMilliseconds.WaitOne();
                    // Detect the difference between waiting and not waiting, but keep the test execution time fast.
                    Thread.Sleep(5);
                    executeFinished = true;
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    executeStarted.WaitOne();
                    Assert.True(stopExecuteInFiveMilliseconds.Set());

                    // Act
                    product.Stop();

                    // Assert
                    Assert.True(executeFinished);
                }
            }
        }

        [Fact]
        public void Stop_WaitsForTaskCompletion()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;
            bool executedOnce = false;
            bool taskCompleted = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(async () =>
                {
                    if (executedOnce)
                    {
                        return;
                    }

                    executedOnce = true;
                    executeStarted.Set();
                    // Detect the difference between waiting and not waiting, but keep the test execution time fast.
                    await Task.Delay(5);
                    taskCompleted = true;
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    executeStarted.WaitOne();
                    Assert.True(stopExecuteInFiveMilliseconds.Set());

                    // Act
                    product.Stop();

                    // Assert
                    Assert.True(taskCompleted);
                }
            }
        }

        [Fact]
        public void Stop_WhenExecuteTaskCompletesCanceled_DoesNotThrow()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;
            bool executedOnce = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        return Task.FromResult(0);
                    }

                    executedOnce = true;
                    executeStarted.Set();
                    TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
                    taskSource.SetCanceled();
                    return taskSource.Task;
                }, interval);

                IBackgroundExceptionDispatcher ignoreDispatcher = CreateIgnoreBackgroundExceptionDispatcher();

                using (IntervalSeparationTimer product = CreateProductUnderTest(command, ignoreDispatcher))
                {
                    product.Start();
                    executeStarted.WaitOne();

                    // Act & Assert
                    Assert.DoesNotThrow(() => product.Stop());
                }
            }
        }

        [Fact]
        public void Stop_WhenExecuteTaskCompletesFaulted_DoesNotThrow()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;
            bool executedOnce = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        return Task.FromResult(0);
                    }

                    executedOnce = true;
                    executeStarted.Set();
                    TaskCompletionSource<object> taskSource = new TaskCompletionSource<object>();
                    taskSource.SetException(new InvalidOperationException());
                    return taskSource.Task;
                }, interval);

                IBackgroundExceptionDispatcher ignoreDispatcher = CreateIgnoreBackgroundExceptionDispatcher();

                using (IntervalSeparationTimer product = CreateProductUnderTest(command, ignoreDispatcher))
                {
                    product.Start();
                    executeStarted.WaitOne();
                    Assert.True(stopExecuteInFiveMilliseconds.Set());

                    // Act & Assert
                    Assert.DoesNotThrow(() => product.Stop());
                }
            }
        }

        [Fact]
        public void Stop_IfDisposed_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Stop());
        }

        [Fact]
        public void Stop_IfNotStarted_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.Stop(), "The timer has not yet been started.");
            }
        }

        [Fact]
        public void Stop_IfAlreadyStopped_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateStubCommand(TimeSpan.FromDays(1));

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                product.Start();
                product.Stop();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.Stop(), "The timer has already been stopped.");
            }
        }

        [Fact]
        public void Cancel_TriggersCommandCancellationToken()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle executeFinished = new ManualResetEvent(initialState: false))
            {
                bool cancellationTokenSignalled = false;

                IIntervalSeparationCommand command = CreateCommand((cancellationToken) =>
                {
                    executeStarted.Set();
                    cancellationTokenSignalled = cancellationToken.WaitHandle.WaitOne(1000);
                    executeFinished.Set();
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    executeStarted.WaitOne();

                    // Act
                    product.Cancel();

                    // Assert
                    executeFinished.WaitOne();
                    Assert.True(cancellationTokenSignalled);

                    // Cleanup
                    product.Stop();
                }
            }
        }

        [Fact]
        public void Cancel_IfDisposed_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Cancel());
        }

        [Fact]
        public void Dispose_IfNotStarted_DoesNotThrow()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [Fact]
        public void Dispose_IfAlreadyDisposed_DoesNotThrow()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [Fact]
        public void Dispose_IfStopped_DoesNotThrow()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateStubCommand(TimeSpan.FromDays(1));
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Start();
            product.Stop();

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [Fact]
        public void Dispose_TriggersCommandCancellationToken()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle executeFinished = new ManualResetEvent(initialState: false))
            {
                bool cancellationTokenSignalled = false;

                IIntervalSeparationCommand command = CreateCommand((cancellationToken) =>
                {
                    executeStarted.Set();
                    cancellationTokenSignalled = cancellationToken.WaitHandle.WaitOne(1000);
                    executeFinished.Set();
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    executeStarted.WaitOne();

                    // Act
                    product.Dispose();

                    // Assert
                    executeFinished.WaitOne();
                    Assert.True(cancellationTokenSignalled);
                }
            }
        }

        [Fact]
        public void Dispose_IfStarted_DoesNotWaitForExecuteToFinish()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecute = new ManualResetEvent(initialState: false))
            {
                bool waitedForCommandToFinish = false;

                IIntervalSeparationCommand command = CreateCommand(() =>
                {
                    executeStarted.Set();
                    stopExecute.WaitOne(1000);
                    waitedForCommandToFinish = true;
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    executeStarted.WaitOne();

                    // Act & Assert
                    product.Dispose();

                    // Assert
                    Assert.False(waitedForCommandToFinish);

                    // Cleanup
                    stopExecute.Set();
                }
            }
        }

        private static IIntervalSeparationCommand CreateCommand(Action execute, TimeSpan separationInterval)
        {
            Mock<IIntervalSeparationCommand> mock = new Mock<IIntervalSeparationCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Callback(execute)
                .Returns(() => Task.FromResult(0));
            mock
                .Setup(c => c.SeparationInterval)
                .Returns(separationInterval);
            return mock.Object;
        }

        private static IIntervalSeparationCommand CreateCommand(Action<CancellationToken> execute,
            TimeSpan separationInterval)
        {
            Mock<IIntervalSeparationCommand> mock = new Mock<IIntervalSeparationCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Callback(execute)
                .Returns(() => Task.FromResult(0));
            mock
                .Setup(c => c.SeparationInterval)
                .Returns(separationInterval);
            return mock.Object;
        }

        private static IIntervalSeparationCommand CreateCommand(Func<Task> executeAsync, TimeSpan separationInterval)
        {
            Mock<IIntervalSeparationCommand> mock = new Mock<IIntervalSeparationCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(executeAsync);
            mock
                .Setup(c => c.SeparationInterval)
                .Returns(separationInterval);
            return mock.Object;
        }

        private static IIntervalSeparationCommand CreateCommand(Action execute, Func<TimeSpan> separationInterval)
        {
            Mock<IIntervalSeparationCommand> mock = new Mock<IIntervalSeparationCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Callback(execute)
                .Returns(() => Task.FromResult(0));
            mock
                .Setup(c => c.SeparationInterval)
                .Returns(separationInterval);
            return mock.Object;
        }

        private static IIntervalSeparationCommand CreateDummyCommand()
        {
            return new Mock<IIntervalSeparationCommand>(MockBehavior.Strict).Object;
        }

        private static IBackgroundExceptionDispatcher CreateIgnoreBackgroundExceptionDispatcher()
        {
            Mock<IBackgroundExceptionDispatcher> mock = new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
            mock.Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()));
            return mock.Object;
        }

        private static IntervalSeparationTimer CreateProductUnderTest(IIntervalSeparationCommand command)
        {
            Mock<IBackgroundExceptionDispatcher> mockDispatcher =
                new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
            mockDispatcher
                .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                .Callback<ExceptionDispatchInfo>(i => i.Throw());
            return CreateProductUnderTest(command, mockDispatcher.Object);
        }

        private static IntervalSeparationTimer CreateProductUnderTest(IIntervalSeparationCommand command,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            return new IntervalSeparationTimer(command, backgroundExceptionDispatcher);
        }

        private static IIntervalSeparationCommand CreateStubCommand(TimeSpan separationInterval)
        {
            Mock<IIntervalSeparationCommand> mock = new Mock<IIntervalSeparationCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(0));
            mock
                .Setup(c => c.SeparationInterval)
                .Returns(separationInterval);
            return mock.Object;
        }
    }
}
