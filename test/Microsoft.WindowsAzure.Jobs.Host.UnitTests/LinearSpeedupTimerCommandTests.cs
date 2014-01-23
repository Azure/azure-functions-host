using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class LinearSpeedupTimerCommandTests
    {
        [TestMethod]
        public void Constructor_IfInnerCommandIsNull_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = null;
            TimeSpan normalInterval = TimeSpan.Zero;
            TimeSpan minimumInterval = TimeSpan.Zero;
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(
                () => CreateProductUnderTest(innerCommand, normalInterval, minimumInterval, failureSpeedupDivisor),
                "innerCommand");
        }

        [TestMethod]
        public void Constructor_IfNormalIntervalIsNegative_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan normalInterval = TimeSpan.FromTicks(-1);
            TimeSpan minimumInterval = TimeSpan.Zero;
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(innerCommand, normalInterval, minimumInterval, failureSpeedupDivisor),
                "normalInterval",
                "The TimeSpan must not be negative.");
        }

        [TestMethod]
        public void Constructor_IfMinimumIntervalIsNegative_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan normalInterval = TimeSpan.Zero;
            TimeSpan minimumInterval = TimeSpan.FromTicks(-1);
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(innerCommand, normalInterval, minimumInterval, failureSpeedupDivisor),
                "minimumInterval",
                "The TimeSpan must not be negative.");
        }

        [TestMethod]
        public void Constructor_IfMinimumIntervalIsGreaterThanNormalInterval_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(1);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(2);
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgument(
                () => CreateProductUnderTest(innerCommand, normalInterval, minimumInterval, failureSpeedupDivisor),
                "minimumInterval",
                "The minimumInterval must not be greater than the normalInterval.");
        }

        [TestMethod]
        public void Constructor_IfFailureSpeedupDivisorIsLessThanOne_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan normalInterval = TimeSpan.Zero;
            TimeSpan minimumInterval = TimeSpan.Zero;
            int failureSpeedupDivisor = 0;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(innerCommand, normalInterval, minimumInterval, failureSpeedupDivisor),
                "failureSpeedupDivisor",
                "The failureSpeedupDivisor must not be less than 1.");
        }

        [TestMethod]
        public void Execute_CallsInnerCommandExecute()
        {
            // Arrange
            bool executed = false;
            ICanFailCommand spy = CreateLambdaCommand(() => executed = true);
            IIntervalSeparationCommand product = CreateProductUnderTest(spy, TimeSpan.Zero, TimeSpan.Zero, 1);

            // Act
            product.Execute();

            // Assert
            Assert.IsTrue(executed);
        }

        [TestMethod]
        public void SeparationInterval_Initially_ReturnsNormalInterval()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand();
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, normalInterval, TimeSpan.Zero, 1);

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.AreEqual(normalInterval, separationInterval);
        }

        [TestMethod]
        public void SeparationInterval_AfterTryExecuteReturnsTrue_ReturnsNormalInterval()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(true);
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, normalInterval, TimeSpan.Zero, 1);
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.AreEqual(normalInterval, separationInterval);
        }

        [TestMethod]
        public void SeparationInterval_AfterTryExecuteReturnsFalse_ReturnsNormalIntervalDividedByDivisor()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            int failureDivisor = 2;
            IIntervalSeparationCommand product = CreateProductUnderTest(command, normalInterval, TimeSpan.Zero,
                failureDivisor);
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            TimeSpan expectedSeparationInterval = new TimeSpan(normalInterval.Ticks / failureDivisor);
            Assert.AreEqual(expectedSeparationInterval, separationInterval);
        }

        [TestMethod]
        public void SeparationInterval_AfterTryExecuteReturnsTrueAfterReturningFalse_ReturnsNormalInterval()
        {
            // Arrange
            bool firstCall = true;
            ICanFailCommand command = CreateLambdaCommand(() =>{
                bool succeeded = !firstCall;
                firstCall = false;
                return succeeded;
            });
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, normalInterval, TimeSpan.Zero, 2);
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.AreEqual(normalInterval, separationInterval);
        }

        [TestMethod]
        public void SeparationInterval_AfterTryExecuteReturnsFalseTwice_ReturnsNormalIntervalDividedByDivisorTwice()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            int failureDivisor = 2;
            IIntervalSeparationCommand product = CreateProductUnderTest(command, normalInterval, TimeSpan.Zero,
                failureDivisor);
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            const int executeCalls = 2;
            TimeSpan expectedSeparationInterval = new TimeSpan(normalInterval.Ticks / (failureDivisor * executeCalls));
            Assert.AreEqual(expectedSeparationInterval, separationInterval);
        }

        [TestMethod]
        public void SeparationInterval_AfterTryExecuteReturnsFalseEnoughTimes_ReturnsMinimumInternal()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(100);
            TimeSpan minimumInternal = TimeSpan.FromMilliseconds(30);
            int failureDivisor = 2;
            IIntervalSeparationCommand product = CreateProductUnderTest(command, normalInterval, minimumInternal,
                failureDivisor);
            product.Execute();
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.AreEqual(minimumInternal, separationInterval);
        }

        private static ICanFailCommand CreateDummyCommand()
        {
            return new LambdaCanFailCommand(() => { throw new NotImplementedException(); });
        }

        private static LinearSpeedupTimerCommand CreateProductUnderTest(ICanFailCommand innerCommand,
            TimeSpan normalInterval, TimeSpan minimumInterval, int failureSpeedupDivisor)
        {
            return new LinearSpeedupTimerCommand(innerCommand, normalInterval, minimumInterval, failureSpeedupDivisor);
        }

        private static ICanFailCommand CreateLambdaCommand(Func<bool> tryExecute)
        {
            return new LambdaCanFailCommand(tryExecute);
        }

        private static ICanFailCommand CreateStubCommand()
        {
            return CreateStubCommand(false);
        }

        private static ICanFailCommand CreateStubCommand(bool tryExecuteReturnValue)
        {
            return new LambdaCanFailCommand(() => tryExecuteReturnValue);
        }

        private class LambdaCanFailCommand : ICanFailCommand
        {
            private readonly Func<bool> _tryExecute;

            public LambdaCanFailCommand(Func<bool> tryExecute)
            {
                _tryExecute = tryExecute;
            }

            public bool TryExecute()
            {
                return _tryExecute.Invoke();
            }
        }
    }
}
