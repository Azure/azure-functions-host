// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Singleton
{
    public class SingletonConfigurationTests
    {
        [Fact]
        public void ConstructorDefaults()
        {
            SingletonConfiguration config = new SingletonConfiguration();

            Assert.Equal(TimeSpan.FromSeconds(15), config.LockPeriod);
            Assert.Equal(TimeSpan.FromMinutes(1), config.LockAcquisitionTimeout);
            Assert.Equal(TimeSpan.FromSeconds(1), config.LockAcquisitionPollingInterval);
        }

        [Fact]
        public void LockPeriod_RangeValidation()
        {
            SingletonConfiguration config = new SingletonConfiguration();

            TimeSpan[] invalidValues = new TimeSpan[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(14),
                TimeSpan.FromSeconds(61)
            };
            foreach (TimeSpan value in invalidValues)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                {
                    config.LockPeriod = value;
                });
            }

            TimeSpan[] validValues = new TimeSpan[]
            {
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(59)
            };
            foreach (TimeSpan value in validValues)
            {
                config.LockPeriod = value;
                Assert.Equal(value, config.LockPeriod);
            }
        }

        [Fact]
        public void LockAcquisitionPollingInterval_RangeValidation()
        {
            SingletonConfiguration config = new SingletonConfiguration();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                config.LockAcquisitionPollingInterval = TimeSpan.FromMilliseconds(499);
            });

            TimeSpan[] validValues = new TimeSpan[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(10)
            };
            foreach (TimeSpan value in validValues)
            {
                config.LockAcquisitionPollingInterval = value;
                Assert.Equal(value, config.LockAcquisitionPollingInterval);
            }
        }

        [Fact]
        public void LockAcquisitionTimeout_RangeValidation()
        {
            SingletonConfiguration config = new SingletonConfiguration();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                config.LockAcquisitionTimeout = TimeSpan.Zero;
            });

            TimeSpan[] validValues = new TimeSpan[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(10)
            };
            foreach (TimeSpan value in validValues)
            {
                config.LockAcquisitionTimeout = value;
                Assert.Equal(value, config.LockAcquisitionTimeout);
            }
        }
    }
}
