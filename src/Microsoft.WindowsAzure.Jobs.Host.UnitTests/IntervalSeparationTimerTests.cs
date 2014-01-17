using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Test;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class IntervalSeparationTimerTests
    {
        [TestMethod]
        public void Constructor_IfCommandIsNull_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => CreateProductUnderTest(command), "command");
        }

        [TestMethod]
        public void Start_IfExecuteFirstIsTrue_Executes()
        {
            // Arrange
            bool executed = false;
            IIntervalSeparationCommand command = CreateLambdaCommand(() => executed = true, TimeSpan.FromDays(1));

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                // Act
                product.Start(executeFirst: true);

                // Assert
                Assert.IsTrue(executed);
            }
        }

        [TestMethod]
        public void Start_IfExecuteFirstIsFalse_DoesNotExecute()
        {
            // Arrange
            bool executed = false;
            IIntervalSeparationCommand command = CreateLambdaCommand(() => executed = true, TimeSpan.FromDays(1));

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                // Act
                product.Start(executeFirst: false);

                // Assert
                Assert.IsFalse(executed);
            }
        }
        
        [TestMethod]
        public void Start_AfterSeparationInterval_Executes()
        {
            // Arrange
            bool executed = false;
            TimeSpan interval = TimeSpan.FromMilliseconds(1);
            IIntervalSeparationCommand command = CreateLambdaCommand(() => executed = true, interval);

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                // Act
                product.Start(executeFirst: false);
                // Let the other thread run, but keep the test execution time fast.
                Thread.Sleep((int)interval.TotalMilliseconds * 15);

                // Assert
                Assert.IsTrue(executed);
            }
        }

        [TestMethod]
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
                IIntervalSeparationCommand command = CreateLambdaCommand(() =>
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
                    product.Start(executeFirst: false);
                    waitForSecondExecution.WaitOne();

                    // Assert
                    // The measured time between may be slightly less than the interval, so approximate.
                    int minimumElapsedMilliseconds = (int)(subsequentInterval.TotalMilliseconds * 0.75);
                    Assert.IsTrue(stopwatch.ElapsedMilliseconds > minimumElapsedMilliseconds);
                }
            }
        }

        [TestMethod]
        public void Start_IfDisposed_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Start(executeFirst: false));
        }

        [TestMethod]
        public void Start_IfStarted_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateStubCommand(TimeSpan.FromDays(1));

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                bool executeFirst = false;
                product.Start(executeFirst);

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.Start(executeFirst),
                    "The timer has already been started; it cannot be restarted.");
            }
        }

        [TestMethod]
        public void Stop_WaitsForExecuteToFinish()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;
            bool executeFinished = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateLambdaCommand(() =>
                {
                    executeStarted.Set();
                    stopExecuteInFiveMilliseconds.WaitOne();
                    // Detect the difference between waiting and not waiting, but keep the test execution time fast.
                    Thread.Sleep(5);
                    executeFinished = true;
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start(executeFirst: false);
                    executeStarted.WaitOne();
                    Assert.IsTrue(stopExecuteInFiveMilliseconds.Set());

                    // Act
                    product.Stop();

                    // Assert
                    Assert.IsTrue(executeFinished);
                }
            }
        }

        [TestMethod]
        public void Stop_IfDisposed_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.ThrowsObjectDisposed(() => product.Stop());
        }

        [TestMethod]
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

        [TestMethod]
        public void Stop_IfAlreadyStopped_Throws()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateStubCommand(TimeSpan.FromDays(1));

            using (IntervalSeparationTimer product = CreateProductUnderTest(command))
            {
                product.Start(executeFirst: false);
                product.Stop();

                // Act & Assert
                ExceptionAssert.ThrowsInvalidOperation(() => product.Stop(), "The timer has already been stopped.");
            }
        }

        [TestMethod]
        public void Dispose_IfNotStarted_DoesNotThrow()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [TestMethod]
        public void Dispose_IfAlreadyDisposed_DoesNotThrow()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateDummyCommand();
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Dispose();

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [TestMethod]
        public void Dispose_IfStopped_DoesNotThrow()
        {
            // Arrange
            IIntervalSeparationCommand command = CreateStubCommand(TimeSpan.FromDays(1));
            IntervalSeparationTimer product = CreateProductUnderTest(command);
            product.Start(executeFirst: false);
            product.Stop();

            // Act & Assert
            ExceptionAssert.DoesNotThrow(() => product.Dispose());
        }

        [TestMethod]
        public void Dispose_IfStarted_WaitsForExecuteToFinish()
        {
            // Arrange
            TimeSpan interval = TimeSpan.Zero;
            bool executeFinished = false;

            using (EventWaitHandle executeStarted = new ManualResetEvent(initialState: false))
            using (EventWaitHandle stopExecuteInFiveMilliseconds = new ManualResetEvent(initialState: false))
            {
                IIntervalSeparationCommand command = CreateLambdaCommand(() =>
                {
                    executeStarted.Set();
                    stopExecuteInFiveMilliseconds.WaitOne();
                    // Detect the difference between waiting and not waiting, but keep the test execution time fast.
                    Thread.Sleep(5);
                    executeFinished = true;
                }, interval);

                using (IntervalSeparationTimer product = CreateProductUnderTest(command))
                {
                    product.Start(executeFirst: false);
                    executeStarted.WaitOne();
                    Assert.IsTrue(stopExecuteInFiveMilliseconds.Set());

                    // Act
                    product.Dispose();

                    // Assert
                    Assert.IsTrue(executeFinished);
                }
            }
        }

        private static IIntervalSeparationCommand CreateDummyCommand()
        {
            return new LambdaIntervalSeparationCommand(() => { throw new NotImplementedException(); },
                () => { throw new NotImplementedException(); });
        }

        private static IIntervalSeparationCommand CreateLambdaCommand(Action execute)
        {
            return new LambdaIntervalSeparationCommand(() => { throw new NotImplementedException(); }, execute);
        }

        private static IIntervalSeparationCommand CreateLambdaCommand(Action execute, TimeSpan separationInterval)
        {
            return new LambdaIntervalSeparationCommand(() => separationInterval, execute);
        }

        private static IIntervalSeparationCommand CreateLambdaCommand(Action execute, Func<TimeSpan> separationInterval)
        {
            return new LambdaIntervalSeparationCommand(separationInterval, execute);
        }

        private static IntervalSeparationTimer CreateProductUnderTest(IIntervalSeparationCommand command)
        {
            return new IntervalSeparationTimer(command);
        }

        private static IIntervalSeparationCommand CreateStubCommand(TimeSpan separationInterval)
        {
            return new LambdaIntervalSeparationCommand(() => separationInterval, () => { });
        }

        private class LambdaIntervalSeparationCommand : IIntervalSeparationCommand
        {
            private readonly Func<TimeSpan> _separationInterval;
            private readonly Action _execute;

            public LambdaIntervalSeparationCommand(Func<TimeSpan> separationInterval, Action execute)
            {
                _separationInterval = separationInterval;
                _execute = execute;
            }

            public TimeSpan SeparationInterval
            {
                get { return _separationInterval.Invoke(); }
            }

            public void Execute()
            {
                _execute.Invoke();
            }
        }
    }
}
