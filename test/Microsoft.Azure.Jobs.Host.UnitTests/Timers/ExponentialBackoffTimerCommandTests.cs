// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.Azure.Jobs.Host.Timers;
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
        public void SeparationInterval_AfterTryExecuteReturnsFalseTwice_ReturnsApproximatelyDoubleMinimumInterval()
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
            AssertInRandomizationRange(separationInterval, minimumInterval, 1);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsFalseThrice_ReturnsApproximatelyQuadrupleMinimumInterval()
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
            AssertInRandomizationRange(separationInterval, minimumInterval, 2);
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
        public void SeparationInterval_AfterTryExecuteReturnsFalseAfterTrue_ReturnsApproximatelyDoubleMinimumInterval()
        {
            // Arrange
            bool firstCall = true;
            ICanFailCommand command = CreateLambdaCommand(() =>
            {
                bool succeeded = firstCall;
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
            AssertInRandomizationRange(separationInterval, minimumInterval, 1);
        }

        [Fact]
        public void SeparationInterval_AfterTryExecuteReturnsFalseEnoughTimes_ReturnsMaximumInternal()
        {
            // Arrange
            ICanFailCommand command = CreateStubCommand(false);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IIntervalSeparationCommand product = CreateProductUnderTest(command, minimumInterval, maximumInterval);

            double randomizationMinimum = 1 - ExponentialBackoffTimerCommand.RandomizationFactor;
            TimeSpan minimumDeltaInterval = new TimeSpan((long)(minimumInterval.Ticks * randomizationMinimum));
            // minimumBackoffInterval = minimumInterval + minimumDeltaInterval * 2 ^ (deltaIteration - 1)
            // when is minimumBackOffInterval first >= maximumInterval?
            // maximumInterval <= minimumInterval + minimumDeltaInterval * 2 ^ (deltaIteration - 1)
            // minimumInterval + minimumDeltaInterval * 2 ^ (deltaIteration - 1) >= maximumInterval
            // minimumDeltaInterval * 2 ^ (deltaIteration - 1) >= maximumInterval - minimumInterval
            // 2 ^ (deltaIteration - 1) >= (maximumInterval - minimumInterval) / minimumDeltaInterval
            // deltaIteration - 1 >= log2(maximumInterval - minimumInterval) / minimumDeltaInterval
            // deltaIteration >= (log2(maximumInterval - minimumInterval) / minimumDeltaInterval) + 1
            int deltaIterationsNeededForMaximumInterval = (int)Math.Ceiling(Math.Log(
                (maximumInterval - minimumInterval).Ticks / minimumDeltaInterval.Ticks, 2)) + 1;

            // Add one for initial minimumInterval interation (before deltaIterations start).
            int iterationsNeededForMaximumInterval = deltaIterationsNeededForMaximumInterval + 1;

            for (int iteration = 0; iteration < iterationsNeededForMaximumInterval; ++iteration)
            {
                product.Execute();
            }

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
            mock.Setup(m => m.TryExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(tryExecute.Invoke()));
            return mock.Object;
        }

        private static ExponentialBackoffTimerCommand CreateProductUnderTest(ICanFailCommand innerCommand,
            TimeSpan minimumInterval, TimeSpan maximumInterval)
        {
            return new ExponentialBackoffTimerCommand(innerCommand, minimumInterval, maximumInterval, minimumInterval);
        }

        private static ICanFailCommand CreateStubCommand(bool result)
        {
            Mock<ICanFailCommand> mock = new Mock<ICanFailCommand>(MockBehavior.Strict);
            mock.Setup(m => m.TryExecuteAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(result));
            return mock.Object;
        }

        private static void AssertInRandomizationRange(TimeSpan separationInterval, TimeSpan minimumInterval,
            int retryCount)
        {
            Assert.InRange(separationInterval.Ticks,
                minimumInterval.Ticks * (1 - ExponentialBackoffTimerCommand.RandomizationFactor) *
                (Math.Pow(2, retryCount-1) + 1),
                minimumInterval.Ticks * (1 + ExponentialBackoffTimerCommand.RandomizationFactor) *
                (Math.Pow(2, retryCount-1) + 1));
        }
    }
}
