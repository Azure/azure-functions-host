// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Timers
{
    public class TaskSeriesTimerTests
    {
        [Fact]
        public void Constructor_IfCommandIsNull_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = null;
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher =
                new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict).Object;
            Task initialWait = Task.FromResult(0);

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(
                () => CreateProductUnderTest(command, backgroundExceptionDispatcher, initialWait), "command");
        }

        [Fact]
        public void Constructor_IfInitialWaitIsNull_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher =
                new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict).Object;
            Task initialWait = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(
                () => CreateProductUnderTest(command, backgroundExceptionDispatcher, initialWait), "initialWait");
        }

        [Fact]
        public void Constructor_IfBackgroundExceptionDispatcherIsNull_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher = null;
            Task initialWait = Task.FromResult(0);

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(
                () => CreateProductUnderTest(command, backgroundExceptionDispatcher, initialWait),
                "backgroundExceptionDispatcher");
        }

        [Fact]
        public void Start_AfterInitialWait_Executes()
        {
            // Arrange
            // Detect the difference between waiting and not waiting, but keep the test execution time fast.
            TimeSpan initialDelay = TimeSpan.FromMilliseconds(50);

            using (EventWaitHandle executedWaitHandle = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    Assert.True(executedWaitHandle.Set()); // Guard
                    return new TaskSeriesCommandResult(wait: Task.Delay(TimeSpan.FromDays(1)));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command, Task.Delay(initialDelay)))
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // Act
                    product.Start();

                    // Assert
                    Assert.True(executedWaitHandle.WaitOne(1000)); // Guard
                    stopwatch.Stop();
                    // Account for the resolution of the system timer; otherwise, the test may fail intermittently
                    // (as the timer may fire slightly before the precise expected value).
                    TimeSpan effectiveActualDelay = AddSystemTimerResolution(stopwatch.Elapsed);
                    AssertGreaterThan(initialDelay, effectiveActualDelay);
                }
            }
        }

        [Fact]
        public void Start_AfterExecute_WaitsForReturnedWait()
        {
            // Arrange
            bool executedOnce = false;
            bool executedTwice = false;
            TimeSpan initialInterval = TimeSpan.Zero;
            // Detect the difference between waiting and not waiting, but keep the test execution time fast.
            TimeSpan subsequentInterval = TimeSpan.FromMilliseconds(5);
            Stopwatch stopwatch = new Stopwatch();

            using (EventWaitHandle waitForSecondExecution = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    if (executedTwice)
                    {
                        return new TaskSeriesCommandResult(wait: Task.Delay(TimeSpan.FromDays(1)));
                    }

                    if (!executedOnce)
                    {
                        stopwatch.Start();
                        executedOnce = true;
                        return new TaskSeriesCommandResult(wait: Task.Delay(subsequentInterval));
                    }
                    else
                    {
                        stopwatch.Stop();
                        executedTwice = true;
                        Assert.True(waitForSecondExecution.Set()); // Guard
                        return new TaskSeriesCommandResult(wait: Task.Delay(initialInterval));
                    }
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    // Act
                    product.Start();

                    // Assert
                    Assert.True(waitForSecondExecution.WaitOne(1000)); // Guard
                    AssertGreaterThan(subsequentInterval, stopwatch.Elapsed);
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
                Func<Task<TaskSeriesCommandResult>> execute = () => { throw expectedException; };
                ITaskSeriesCommand command = CreateCommand(execute);

                Mock<IBackgroundExceptionDispatcher> backgroundExceptionDispatcherMock =
                    new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
                ExceptionDispatchInfo exceptionInfo = null;
                backgroundExceptionDispatcherMock
                    .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                    .Callback<ExceptionDispatchInfo>((i) =>
                    {
                        exceptionInfo = i;
                        Assert.True(exceptionDispatchedWaitHandle.Set()); // Guard
                    });
                IBackgroundExceptionDispatcher backgroundExceptionDispatcher = backgroundExceptionDispatcherMock.Object;

                using (ITaskSeriesTimer product = CreateProductUnderTest(command, backgroundExceptionDispatcher))
                {
                    // Act
                    product.Start();

                    // Assert
                    bool executed = exceptionDispatchedWaitHandle.WaitOne(1000);
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

            ITaskSeriesCommand command = CreateCommand(() =>
            {
                TaskCompletionSource<TaskSeriesCommandResult> taskSource =
                    new TaskCompletionSource<TaskSeriesCommandResult>();
                taskSource.SetException(expectedException);
                return taskSource.Task;
            });

            using (EventWaitHandle exceptionDispatchedWaitHandle = new ManualResetEvent(initialState: false))
            {
                Mock<IBackgroundExceptionDispatcher> backgroundExceptionDispatcherMock =
                new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
                ExceptionDispatchInfo exceptionInfo = null;
                backgroundExceptionDispatcherMock
                    .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                    .Callback<ExceptionDispatchInfo>((i) =>
                    {
                        exceptionInfo = i;
                        Assert.True(exceptionDispatchedWaitHandle.Set()); // Guard
                    });
                IBackgroundExceptionDispatcher backgroundExceptionDispatcher = backgroundExceptionDispatcherMock.Object;

                using (ITaskSeriesTimer product = CreateProductUnderTest(command, backgroundExceptionDispatcher))
                {
                    // Act
                    product.Start();

                    // Assert
                    Assert.True(exceptionDispatchedWaitHandle.WaitOne(1000)); // Guard
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

            using (EventWaitHandle executedTwiceWaitHandle = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        Assert.True(executedTwiceWaitHandle.Set()); // Guard
                        return Task.FromResult(new TaskSeriesCommandResult(Task.Delay(TimeSpan.FromDays(1))));
                    }

                    executedOnce = true;
                    TaskCompletionSource<TaskSeriesCommandResult> taskSource =
                        new TaskCompletionSource<TaskSeriesCommandResult>();
                    taskSource.SetCanceled();
                    return taskSource.Task;
                });

                Mock<IBackgroundExceptionDispatcher> backgroundExceptionDispatcherMock =
                    new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
                int backgroundExceptionCalls = 0;
                backgroundExceptionDispatcherMock
                    .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                    .Callback(() => backgroundExceptionCalls++);
                IBackgroundExceptionDispatcher backgroundExceptionDispatcher = backgroundExceptionDispatcherMock.Object;

                using (ITaskSeriesTimer product = CreateProductUnderTest(command, backgroundExceptionDispatcher))
                {
                    // Act
                    product.Start();

                    // Assert
                    Assert.True(executedTwiceWaitHandle.WaitOne(1000)); // Guard
                    Assert.Equal(0, backgroundExceptionCalls);
                }
            }
        }

        [Fact]
        public void Start_IfDisposed_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            ITaskSeriesTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Start());
        }

        [Fact]
        public void Start_IfStarted_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateStubCommand(TimeSpan.FromDays(1));

            using (ITaskSeriesTimer product = CreateProductUnderTest(command))
            {
                product.Start();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.Start(),
                    "The timer has already been started; it cannot be restarted.");
            }
        }

        [Fact]
        public void StopAsync_TriggersCommandCancellationToken()
        {
            // Arrange
            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle executeFinished = new ManualResetEvent(initialState: false))
            {
                bool cancellationTokenSignalled = false;

                ITaskSeriesCommand command = CreateCommand((cancellationToken) =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    cancellationTokenSignalled = cancellationToken.WaitHandle.WaitOne(1000);
                    Assert.True(executeFinished.Set()); // Guard
                    return new TaskSeriesCommandResult(wait: Task.Delay(0));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    CancellationToken cancellationToken = CancellationToken.None;

                    // Act
                    Task task = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(task);
                    task.GetAwaiter().GetResult();
                    Assert.True(executeFinished.WaitOne(1000)); // Guard
                    Assert.True(cancellationTokenSignalled);
                }
            }
        }

        [Fact]
        public void StopAsync_WaitsForExecuteToFinishToCompleteTask()
        {
            // Arrange
            bool executeFinished = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    Assert.True(stopExecuteInFiveMilliseconds.WaitOne(1000)); // Guard
                    // Detect the difference between waiting and not waiting, but keep the test execution time fast.
                    Thread.Sleep(5);
                    executeFinished = true;
                    return new TaskSeriesCommandResult(wait: Task.Delay(0));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard
                    Assert.True(stopExecuteInFiveMilliseconds.Set()); // Guard

                    CancellationToken cancellationToken = CancellationToken.None;

                    // Act
                    Task task = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(task);
                    task.GetAwaiter().GetResult();
                    Assert.True(executeFinished);
                }
            }
        }

        [Fact]
        public void StopAsync_WhenCanceled_DoesNotWaitForExecuteToFinishToCompleteTask()
        {
            // Arrange
            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    return new TaskSeriesCommandResult(wait: Task.Delay(2000));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    CancellationToken cancellationToken = new CancellationToken(canceled: true);

                    // Act
                    Task task = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(task);
                    Assert.True(task.WaitUntilCompleted(1000));
                    Assert.True(task.IsCompleted);
                }
            }
        }

        [Fact]
        public void StopAsync_DoesNotWaitForInitialWaitToCompleteTask()
        {
            // Arrange
            ITaskSeriesCommand command = CreateStubCommand(TimeSpan.Zero);

            using (ITaskSeriesTimer product = CreateProductUnderTest(command, initialWait: Task.Delay(2000)))
            {
                product.Start();

                // Wait for the background thread to enter the initialWait.
                Thread.Sleep(5);

                CancellationToken cancellationToken = CancellationToken.None;

                // Act
                Task task = product.StopAsync(cancellationToken);

                // Assert
                Assert.NotNull(task);
                Assert.True(task.WaitUntilCompleted(1000));
                Assert.True(task.IsCompleted);
            }
        }

        [Fact]
        public void StopAsync_DoesNotWaitForSubsequentWaitToCompleteTask()
        {
            // Arrange
            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    return new TaskSeriesCommandResult(wait: Task.Delay(2000));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    // Wait for the background thread to enter the long wait.
                    Thread.Sleep(5);

                    CancellationToken cancellationToken = CancellationToken.None;

                    // Act
                    Task task = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(task);
                    Assert.True(task.WaitUntilCompleted(1000));
                    Assert.True(task.IsCompleted);
                }
            }
        }

        [Fact]
        public void StopAsync_TriggersNotExecutingAgain()
        {
            // Arrange
            using (EventWaitHandle executedOnceWaitHandle = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesTimer product = null;
                Task stop = null;
                bool executedOnce = false;
                bool executedTwice = false;

                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    if (!executedOnce)
                    {
                        stop = product.StopAsync(CancellationToken.None);
                        Assert.True(executedOnceWaitHandle.Set()); // Guard
                        executedOnce = true;
                        return new TaskSeriesCommandResult(wait: Task.Delay(0));
                    }
                    else
                    {
                        executedTwice = true;
                        return new TaskSeriesCommandResult(wait: Task.Delay(0));
                    }
                });

                using (product = CreateProductUnderTest(command))
                {
                    product.Start();

                    // Act
                    bool executed = executedOnceWaitHandle.WaitOne(1000);

                    // Assert
                    Assert.True(executed); // Guard
                    Assert.NotNull(stop);
                    stop.GetAwaiter().GetResult();
                    Assert.False(executedTwice);
                }
            }
        }

        [Fact]
        public void StopAsync_WaitsForTaskCompletionToCompleteTask()
        {
            // Arrange
            bool executedOnce = false;
            bool taskCompleted = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(async () =>
                {
                    if (executedOnce)
                    {
                        return new TaskSeriesCommandResult(wait: Task.Delay(0));
                    }

                    executedOnce = true;
                    Assert.True(executeStarted.Set()); // Guard
                    // Detect the difference between waiting and not waiting, but keep the test execution time fast.
                    await Task.Delay(5);
                    taskCompleted = true;
                    return new TaskSeriesCommandResult(wait: Task.Delay(0));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard
                    Assert.True(stopExecuteInFiveMilliseconds.Set()); // Guard

                    CancellationToken cancellationToken = CancellationToken.None;

                    // Act
                    Task stopTask = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(stopTask);
                    stopTask.GetAwaiter().GetResult();
                    Assert.True(taskCompleted);
                }
            }
        }

        [Fact]
        public void StopAsync_WhenExecuteTaskCompletesCanceled_DoesNotThrowOrFault()
        {
            // Arrange
            bool executedOnce = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        return Task.FromResult(new TaskSeriesCommandResult(wait: Task.Delay(0)));
                    }

                    executedOnce = true;
                    Assert.True(executeStarted.Set()); // Guard
                    TaskCompletionSource<TaskSeriesCommandResult> taskSource =
                        new TaskCompletionSource<TaskSeriesCommandResult>();
                    taskSource.SetCanceled();
                    return taskSource.Task;
                });

                IBackgroundExceptionDispatcher ignoreDispatcher = CreateIgnoreBackgroundExceptionDispatcher();

                using (ITaskSeriesTimer product = CreateProductUnderTest(command, ignoreDispatcher))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    CancellationToken cancellationToken = CancellationToken.None;

                    // Act
                    Task task = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(task);
                    task.GetAwaiter().GetResult();
                }
            }
        }

        [Fact]
        public void StopAsync_WhenExecuteTaskCompletesFaulted_DoesNotThrowOrFault()
        {
            // Arrange
            bool executedOnce = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    if (executedOnce)
                    {
                        return Task.FromResult(new TaskSeriesCommandResult(wait: Task.Delay(0)));
                    }

                    executedOnce = true;
                    Assert.True(executeStarted.Set()); // Guard
                    TaskCompletionSource<TaskSeriesCommandResult> taskSource =
                        new TaskCompletionSource<TaskSeriesCommandResult>();
                    taskSource.SetException(new InvalidOperationException());
                    return taskSource.Task;
                });

                IBackgroundExceptionDispatcher ignoreDispatcher = CreateIgnoreBackgroundExceptionDispatcher();

                using (ITaskSeriesTimer product = CreateProductUnderTest(command, ignoreDispatcher))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    CancellationToken cancellationToken = CancellationToken.None;

                    // Act
                    Task task = product.StopAsync(cancellationToken);

                    // Assert
                    Assert.NotNull(task);
                    task.GetAwaiter().GetResult();
                }
            }
        }

        [Fact]
        public void StopAsync_IfDisposed_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            ITaskSeriesTimer product = CreateProductUnderTest(command);
            product.Dispose();

            CancellationToken cancellationToken = CancellationToken.None;

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.StopAsync(cancellationToken));
        }

        [Fact]
        public void StopAsync_IfNotStarted_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();

            using (ITaskSeriesTimer product = CreateProductUnderTest(command))
            {
                CancellationToken cancellationToken = CancellationToken.None;

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.StopAsync(cancellationToken),
                    "The timer has not yet been started.");
            }
        }

        [Fact]
        public void StopAsync_IfAlreadyStopped_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateStubCommand(TimeSpan.Zero);

            using (ITaskSeriesTimer product = CreateProductUnderTest(command))
            {
                product.Start();
                product.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

                CancellationToken cancellationToken = CancellationToken.None;

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.StopAsync(cancellationToken),
                    "The timer has already been stopped.");
            }
        }

        [Fact]
        public void Cancel_TriggersCommandCancellationToken()
        {
            // Arrange
            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle executeFinished = new ManualResetEvent(initialState: false))
            {
                bool cancellationTokenSignalled = false;

                ITaskSeriesCommand command = CreateCommand((cancellationToken) =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    cancellationTokenSignalled = cancellationToken.WaitHandle.WaitOne(1000);
                    Assert.True(executeFinished.Set()); // Guard
                    return new TaskSeriesCommandResult(wait: Task.Delay(0));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    // Act
                    product.Cancel();

                    // Assert
                    Assert.True(executeFinished.WaitOne(1000)); // Guard
                    Assert.True(cancellationTokenSignalled);

                    // Cleanup
                    product.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
            }
        }

        [Fact]
        public void Cancel_IfDisposed_Throws()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            ITaskSeriesTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Cancel());
        }

        [Fact]
        public void Dispose_IfNotStarted_DoesNotThrow()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            ITaskSeriesTimer product = CreateProductUnderTest(command);

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [Fact]
        public void Dispose_IfAlreadyDisposed_DoesNotThrow()
        {
            // Arrange
            ITaskSeriesCommand command = CreateDummyCommand();
            ITaskSeriesTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [Fact]
        public void Dispose_IfStopped_DoesNotThrow()
        {
            // Arrange
            ITaskSeriesCommand command = CreateStubCommand(TimeSpan.Zero);
            ITaskSeriesTimer product = CreateProductUnderTest(command);
            product.Start();
            product.StopAsync(CancellationToken.None).GetAwaiter().GetResult();

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

                ITaskSeriesCommand command = CreateCommand((cancellationToken) =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    cancellationTokenSignalled = cancellationToken.WaitHandle.WaitOne(1000);
                    Assert.True(executeFinished.Set()); // Guard
                    return new TaskSeriesCommandResult(wait: Task.Delay(0));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    // Act
                    product.Dispose();

                    // Assert
                    Assert.True(executeFinished.WaitOne(1000)); // Guard
                    Assert.True(cancellationTokenSignalled);
                }
            }
        }

        [Fact]
        public void Dispose_IfStarted_DoesNotWaitForExecuteToFinish()
        {
            // Arrange
            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecute = new ManualResetEvent(initialState: false))
            {
                bool waitedForCommandToFinish = false;

                ITaskSeriesCommand command = CreateCommand(() =>
                {
                    Assert.True(executeStarted.Set()); // Guard
                    stopExecute.WaitOne(1000);
                    waitedForCommandToFinish = true;
                    return new TaskSeriesCommandResult(wait: Task.Delay(0));
                });

                using (ITaskSeriesTimer product = CreateProductUnderTest(command))
                {
                    product.Start();
                    Assert.True(executeStarted.WaitOne(1000)); // Guard

                    // Act & Assert
                    product.Dispose();

                    // Assert
                    Assert.False(waitedForCommandToFinish);

                    // Cleanup
                    Assert.True(stopExecute.Set()); // Guard
                }
            }
        }

        private static TimeSpan AddSystemTimerResolution(TimeSpan value)
        {
            // The default system timer resolution is around 15.6ms:
            // From http://msdn.microsoft.com/en-us/library/windows/hardware/ff545575(v=vs.85).aspx:
            // "Standard framework timers have an accuracy that matches the system clock tick interval, which is by
            // default 15.6 milliseconds."
            TimeSpan systemTimerResolution = TimeSpan.FromMilliseconds(15 + 1);
            return value + systemTimerResolution;
        }

        private static void AssertGreaterThan(TimeSpan expected, TimeSpan actual)
        {
            string message = String.Format("{0} > {1}", actual, expected);
            Assert.True(actual > expected, message);
        }

        private static ITaskSeriesCommand CreateCommand(Func<TaskSeriesCommandResult> execute)
        {
            Mock<ITaskSeriesCommand> mock = new Mock<ITaskSeriesCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(execute.Invoke()));
            return mock.Object;
        }

        private static ITaskSeriesCommand CreateCommand(Func<CancellationToken, TaskSeriesCommandResult> execute)
        {
            Mock<ITaskSeriesCommand> mock = new Mock<ITaskSeriesCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>((c) => Task.FromResult(execute.Invoke(c)));
            return mock.Object;
        }

        private static ITaskSeriesCommand CreateCommand(Func<Task<TaskSeriesCommandResult>> execute)
        {
            Mock<ITaskSeriesCommand> mock = new Mock<ITaskSeriesCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(() => execute.Invoke());
            return mock.Object;
        }

        private static ITaskSeriesCommand CreateDummyCommand()
        {
            return new Mock<ITaskSeriesCommand>(MockBehavior.Strict).Object;
        }

        private static IBackgroundExceptionDispatcher CreateStackExceptionDispatcher()
        {
            Mock<IBackgroundExceptionDispatcher> mock = new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
            mock
                .Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()))
                .Callback<ExceptionDispatchInfo>(i => i.Throw());
            return mock.Object;
        }

        private static IBackgroundExceptionDispatcher CreateIgnoreBackgroundExceptionDispatcher()
        {
            Mock<IBackgroundExceptionDispatcher> mock = new Mock<IBackgroundExceptionDispatcher>(MockBehavior.Strict);
            mock.Setup(d => d.Throw(It.IsAny<ExceptionDispatchInfo>()));
            return mock.Object;
        }

        private static TaskSeriesTimer CreateProductUnderTest(ITaskSeriesCommand command)
        {
            return CreateProductUnderTest(command, CreateStackExceptionDispatcher(), Task.Delay(0));
        }

        private static TaskSeriesTimer CreateProductUnderTest(ITaskSeriesCommand command, Task initialWait)
        {
            return CreateProductUnderTest(command, CreateStackExceptionDispatcher(), initialWait);
        }

        private static TaskSeriesTimer CreateProductUnderTest(ITaskSeriesCommand command,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            return CreateProductUnderTest(command, backgroundExceptionDispatcher, Task.Delay(0));
        }

        private static TaskSeriesTimer CreateProductUnderTest(ITaskSeriesCommand command,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher, Task initialWait)
        {
            return new TaskSeriesTimer(command, backgroundExceptionDispatcher, initialWait);
        }

        private static ITaskSeriesCommand CreateStubCommand(TimeSpan separationInterval)
        {
            Mock<ITaskSeriesCommand> mock = new Mock<ITaskSeriesCommand>(MockBehavior.Strict);
            mock
                .Setup(c => c.ExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(new TaskSeriesCommandResult(wait: Task.Delay(separationInterval))));
            return mock.Object;
        }
    }
}
