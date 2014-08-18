// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Timers
{
    public class LinearSpeedupStrategyTests
    {
        [Fact]
        public void Constructor_IfNormalIntervalIsNegative_Throws()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromTicks(-1);
            TimeSpan minimumInterval = TimeSpan.Zero;
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(normalInterval, minimumInterval, failureSpeedupDivisor),
                "normalInterval",
                "The TimeSpan must not be negative.");
        }

        [Fact]
        public void Constructor_IfMinimumIntervalIsNegative_Throws()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.Zero;
            TimeSpan minimumInterval = TimeSpan.FromTicks(-1);
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(normalInterval, minimumInterval, failureSpeedupDivisor),
                "minimumInterval",
                "The TimeSpan must not be negative.");
        }

        [Fact]
        public void Constructor_IfMinimumIntervalIsGreaterThanNormalInterval_Throws()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(1);
            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(2);
            int failureSpeedupDivisor = 1;

            // Act & Assert
            ExceptionAssert.ThrowsArgument(
                () => CreateProductUnderTest(normalInterval, minimumInterval, failureSpeedupDivisor),
                "minimumInterval",
                "The minimumInterval must not be greater than the normalInterval.");
        }

        [Fact]
        public void Constructor_IfFailureSpeedupDivisorIsLessThanOne_Throws()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.Zero;
            TimeSpan minimumInterval = TimeSpan.Zero;
            int failureSpeedupDivisor = 0;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentOutOfRange(
                () => CreateProductUnderTest(normalInterval, minimumInterval, failureSpeedupDivisor),
                "failureSpeedupDivisor",
                "The failureSpeedupDivisor must not be less than 1.");
        }

        [Fact]
        public void GetNextDelay_WhenFirstExecutionSucceeded_ReturnsNormalInterval()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            IDelayStrategy product = CreateProductUnderTest(normalInterval, TimeSpan.Zero, 1);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: true);

            // Assert
            Assert.Equal(normalInterval, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenFirstExecutionFailed_ReturnsNormalIntervalDividedByDivisor()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            int failureDivisor = 2;
            IDelayStrategy product = CreateProductUnderTest(normalInterval, TimeSpan.Zero, failureDivisor);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            TimeSpan expectedNextDelay = new TimeSpan(normalInterval.Ticks / failureDivisor);
            Assert.Equal(expectedNextDelay, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenSecondExecutionSucceededAfterFailing_ReturnsNormalInterval()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            IDelayStrategy product = CreateProductUnderTest(normalInterval, TimeSpan.Zero, 2);
            product.GetNextDelay(executionSucceeded: false);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: true);

            // Assert
            Assert.Equal(normalInterval, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenSecondExecutionFailedAgain_ReturnsNormalIntervalDividedByDivisorTwice()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(123);
            int failureDivisor = 2;
            IDelayStrategy product = CreateProductUnderTest(normalInterval, TimeSpan.Zero, failureDivisor);
            product.GetNextDelay(executionSucceeded: false);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            const int executeCalls = 2;
            TimeSpan expectedNextDelay = new TimeSpan(normalInterval.Ticks / (failureDivisor * executeCalls));
            Assert.Equal(expectedNextDelay, nextDelay);
        }

        [Fact]
        public void GetNextDelay_WhenExecutionFailedAgainEnoughTimes_ReturnsMinimumInternal()
        {
            // Arrange
            TimeSpan normalInterval = TimeSpan.FromMilliseconds(100);
            TimeSpan minimumInternal = TimeSpan.FromMilliseconds(30);
            int failureDivisor = 2;
            IDelayStrategy product = CreateProductUnderTest(normalInterval, minimumInternal, failureDivisor);
            product.GetNextDelay(executionSucceeded: false);
            product.GetNextDelay(executionSucceeded: false);

            // Act
            TimeSpan nextDelay = product.GetNextDelay(executionSucceeded: false);

            // Assert
            Assert.Equal(minimumInternal, nextDelay);
        }

        private static LinearSpeedupStrategy CreateProductUnderTest(TimeSpan normalInterval, TimeSpan minimumInterval,
            int failureSpeedupDivisor)
        {
            return new LinearSpeedupStrategy(normalInterval, minimumInterval, failureSpeedupDivisor);
        }
    }
}
