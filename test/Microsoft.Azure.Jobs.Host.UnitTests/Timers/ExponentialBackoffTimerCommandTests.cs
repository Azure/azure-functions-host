using System;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Moq;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Timers
{
    public class ExponentialBackoffTimerCommandTests
    {
        [Fact]
        public void Constructor_IfInnerCommandIsNull_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = null;
            TimeSpan minimumInterval = TimeSpan.Zero;
            TimeSpan maximumInterval = TimeSpan.Zero;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(
                () => CreateProductUnderTest(innerCommand, minimumInterval, maximumInterval),
                "innerCommand");
        }

        [Fact]
        public void Constructor_IfMinimumIntervalIsNegative_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan minimumInterval = TimeSpan.FromTicks(-1);
            TimeSpan maximumInterval = TimeSpan.Zero;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(innerCommand, minimumInterval, maximumInterval),
                "minimumInterval",
                "The TimeSpan must not be negative.");
        }

        [Fact]
        public void Constructor_IfMaximumIntervalIsNegative_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan minimumInterval = TimeSpan.Zero;
            TimeSpan maximumInterval = TimeSpan.FromTicks(-1);

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(innerCommand, minimumInterval, maximumInterval),
                "maximumInterval",
                "The TimeSpan must not be negative.");
        }

        [Fact]
        public void Constructor_IfMinimumIntervalIsGreaterThanMaximumInterval_Throws()
        {
            // Arrange
            ICanFailCommand innerCommand = CreateDummyCommand();
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(2);
            TimeSpan maximumInterval = TimeSpan.FromMilliseconds(1);

            // Act & Assert
            ExceptionAssert.ThrowsArgument(
                () => CreateProductUnderTest(innerCommand, minimumInterval, maximumInterval),
                "minimumInterval",
                "The minimumInterval must not be greater than the maximumInterval.");
        }

        [Fact]
        public void Execute_CallsInnerCommandTryExecute()
        {
            // Arrange
            bool executed = false;
            ICanFailCommand spy = CreateLambdaCommand(() => executed = true);
            IIntervalSeparationCommand product = CreateProductUnderTest(spy, TimeSpan.Zero, TimeSpan.Zero);

            // Act
            product.Execute();

            // Assert
            Assert.True(executed);
        }

        [Fact]
        public void SeparationInterval_Initially_ReturnsZero()
        {
            // Arrange
            ICanFailCommand command = CreateDummyCommand();
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(1);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(2);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.Equal(TimeSpan.Zero, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsTrue_ReturnsMinimumInterval()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(true);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.Equal(minimumInterval, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsFalseOnce_ReturnsMinimumInterval()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.Equal(minimumInterval, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsFalseTwice_ReturnsDoubleMinimumInterval()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            TimeSpan expectedSeparationInterval = new TimeSpan(minimumInterval.Ticks * 2);
            Assert.Equal(expectedSeparationInterval, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsFalseThrice_ReturnsQuadrupleMinimumInterval()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            TimeSpan expectedSeparationInterval = new TimeSpan(minimumInterval.Ticks * 4);
            Assert.Equal(expectedSeparationInterval, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsTrueAfterReturningFalse_ReturnsMinimumInterval()
        {
            // Arrange
            bool firstCall = true;
            ICanFailCommand command = CreateLambdaCommand(() =>
            {
                bool succeeded = !firstCall;
                firstCall = false;
                return succeeded;
            });
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.Equal(minimumInterval, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteStartsReturningFalseAgain_ReturnsMinimumInterval()
        {
            // Arrange
            int call = 0;
            ICanFailCommand command = CreateLambdaCommand(() =>
            {
                if (call == 0)
                {
                    call++;
                    return false;
                }
                else if (call == 1)
                {
                    call++;
                    return true;
                }
                else
                {
                    return false;
                }
            });
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.Equal(minimumInterval, separationInterval);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsFalseEnoughTimes_ReturnsMaximumInternal()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);
            product.Execute();
            product.Execute();
            product.Execute();
            product.Execute();
            product.Execute();
            product.Execute();
            product.Execute();

            // Act
            TimeSpan separationInterval = product.SeparationInterval;

            // Assert
            Assert.Equal(maximumInterval, separationInterval);
        }

        private static ICanFailCommand CreateDummyCommand()
        {
            return new Mock<ICanFailCommand>(MockBehavior.Strict).Object;
        }

        private static ICanFailCommand CreateLambdaCommand(Func<bool> tryExecute)
        {
            Mock<ICanFailCommand> mock = new Mock<ICanFailCommand>(MockBehavior.Strict);
            mock.Setup(m => m.TryExecute()).Returns(tryExecute);
            return mock.Object;
        }

        private static ExponentialBackoffTimerCommand CreateProductUnderTest(ICanFailCommand innerCommand,
            TimeSpan minimumInterval, TimeSpan maximumInterval)
        {
            return new ExponentialBackoffTimerCommand(innerCommand, minimumInterval, maximumInterval);
        }

        private static ICanFailCommand CreateStubCommand(bool result)
        {
            Mock<ICanFailCommand> mock = new Mock<ICanFailCommand>(MockBehavior.Strict);
            mock.Setup(m => m.TryExecute()).Returns(result);
            return mock.Object;
        }
    }
}
