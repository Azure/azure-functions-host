// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.Azure.Jobs.Host.Timers;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Timers
{
    public class RandomizedExponentialBackoffStrategyTests
    {
        [Fact]
        public void Constructor_IfMinimumIntervalIsNegative_Throws()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromTicks(-1);
            TimeSpan maximumInterval = TimeSpan.Zero;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(minimumInterval, maximumInterval),
                "minimumInterval",
                "The TimeSpan must not be negative.");
        }

        [Fact]
        public void Constructor_IfMaximumIntervalIsNegative_Throws()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.Zero;
            TimeSpan maximumInterval = TimeSpan.FromTicks(-1);

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(minimumInterval, maximumInterval),
                "maximumInterval",
                "The TimeSpan must not be negative.");
        }

        [Fact]
        public void Constructor_IfMinimumIntervalIsGreaterThanMaximumInterval_Throws()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(2);
            TimeSpan maximumInterval = TimeSpan.FromMilliseconds(1);

            // Act & Assert
            ExceptionAssert.ThrowsArgument(
                () => CreateProductUnderTest(minimumInterval, maximumInterval),
                "minimumInterval",
                "The minimumInterval must not be greater than the maximumInterval.");
        }

        [Fact]
        public void GetNextDelay_WhenFirstExecutionSucceeded_ReturnsMinimumInterval()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: true);

            // Assert
            Assert.Equal(minimumInterval, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenFirstExecutionFailed_ReturnsMinimumInterval()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            Assert.Equal(minimumInterval, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenSecondExecutionFailedAgain_ReturnsApproximatelyDoubleMinimumInterval()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);
            product.GetNextDelay(executionSucceeded: false);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            AssertInRandomizationRange(minimumInterval, 1, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenThirdExecutionFailedAgain_ReturnsApproximatelyQuadrupleMinimumInterval()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);
            product.GetNextDelay(executionSucceeded: false);
            product.GetNextDelay(executionSucceeded: false);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            AssertInRandomizationRange(minimumInterval, 2, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenExecutionSuccededAfterPreviouslyFailing_ReturnsMinimumInterval()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);
            product.GetNextDelay(executionSucceeded: false);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: true);

            // Assert
            Assert.Equal(minimumInterval, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenExecutionFailedAfterPreviouslySucceeding_ReturnsRoughlyDoubleMinimumInterval()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);
            product.GetNextDelay(executionSucceeded: true);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            AssertInRandomizationRange(minimumInterval, 1, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenExecutionFailedAgainEnoughTimes_ReturnsMaximumInternal()
        {
            // Arrange
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(123);
            TimeSpan maximumInterval = TimeSpan.FromSeconds(4);
            IDelayStrategy product = CreateProductUnderTest(minimumInterval, maximumInterval);

            double randomizationMinimum = 1 - RandomizedExponentialBackoffStrategy.RandomizationFactor;
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

            for (int iteration = 0; iteration < iterationsNeededForMaximumInterval - 1; iteration++)
            {
                product.GetNextDelay(executionSucceeded: false);
            }

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            Assert.Equal(maximumInterval, nextDelay);
        }

        private static RandomizedExponentialBackoffStrategy CreateProductUnderTest(TimeSpan minimumInterval,
            TimeSpan maximumInterval)
        {
            return new RandomizedExponentialBackoffStrategy(minimumInterval, maximumInterval, minimumInterval);
        }

        private static void AssertInRandomizationRange(TimeSpan minimumInterval, int retryCount, TimeSpan actual)
        {
            Assert.InRange(actual.Ticks,
                minimumInterval.Ticks * (1 - RandomizedExponentialBackoffStrategy.RandomizationFactor) *
                (Math.Pow(2, retryCount-1) + 1),
                minimumInterval.Ticks * (1 + RandomizedExponentialBackoffStrategy.RandomizationFactor) *
                (Math.Pow(2, retryCount-1) + 1));
        }
    }
}
