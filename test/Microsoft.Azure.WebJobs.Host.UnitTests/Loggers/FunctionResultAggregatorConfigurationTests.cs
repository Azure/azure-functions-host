// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.


using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class FunctionResultAggregatorConfigurationTests
    {
        [Fact]
        public void DefaultValues()
        {
            var config = new FunctionResultAggregatorConfiguration();

            Assert.Equal(TimeSpan.FromSeconds(30), config.FlushTimeout);
            Assert.Equal(1000, config.BatchSize);
            Assert.Equal(true, config.IsEnabled);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(10001, true)]
        [InlineData(1, false)]
        [InlineData(10000, false)]
        public void BatchSize_Limits(int batchSize, bool throws)
        {
            var config = new FunctionResultAggregatorConfiguration();
            ArgumentOutOfRangeException caughtEx = null;

            try
            {
                config.BatchSize = batchSize;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caughtEx = ex;
            }

            Assert.Equal(throws, caughtEx != null);
        }

        public static object[][] TimeSpanData = new object[][] {
            new object[] { TimeSpan.FromSeconds(300), false },
            new object[] { TimeSpan.FromSeconds(0), true },
            new object[] { TimeSpan.FromSeconds(301), true },
            new object[] { TimeSpan.FromSeconds(1), false },
        };

        [Theory]
        [MemberData(nameof(TimeSpanData))]
        public void FlushTimeout_Limits(TimeSpan flushTimeout, bool throws)
        {
            var config = new FunctionResultAggregatorConfiguration();
            ArgumentOutOfRangeException caughtEx = null;

            try
            {
                config.FlushTimeout = flushTimeout;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caughtEx = ex;
            }

            Assert.Equal(throws, caughtEx != null);
        }
    }
}
