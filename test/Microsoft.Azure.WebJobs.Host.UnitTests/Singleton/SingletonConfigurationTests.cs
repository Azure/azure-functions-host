// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
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
            Assert.Equal(TimeSpan.FromSeconds(60), config.ListenerLockPeriod);
            Assert.Equal(TimeSpan.MaxValue, config.LockAcquisitionTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), config.LockAcquisitionPollingInterval);
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
        public void ListenerLockPeriod_RangeValidation()
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
                    config.ListenerLockPeriod = value;
                });
            }

            TimeSpan[] validValues = new TimeSpan[]
            {
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(59)
            };
            foreach (TimeSpan value in validValues)
            {
                config.ListenerLockPeriod = value;
                Assert.Equal(value, config.ListenerLockPeriod);
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

        [Fact]
        public void ListenerLockRecoveryPollingInterval_RangeValidation()
        {
            SingletonConfiguration config = new SingletonConfiguration();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                config.ListenerLockRecoveryPollingInterval = TimeSpan.FromMilliseconds(14999);
            });

            TimeSpan[] validValues = new TimeSpan[]
            {
                TimeSpan.MaxValue,
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMinutes(5)
            };
            foreach (TimeSpan value in validValues)
            {
                config.ListenerLockRecoveryPollingInterval = value;
                Assert.Equal(value, config.ListenerLockRecoveryPollingInterval);
            }
        }
    }
}
