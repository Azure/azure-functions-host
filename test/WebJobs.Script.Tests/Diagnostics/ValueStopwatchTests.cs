// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    /// <summary>
    /// Based on: https://github.com/dotnet/runtime/blob/main/src/libraries/Common/tests/Extensions/ValueStopwatchTest.cs
    /// </summary>
    public class ValueStopwatchTests
    {
        [Fact]
        public void IsActiveIsFalseForDefaultValueStopwatch()
        {
            Assert.False(default(ValueStopwatch).IsActive);
        }

        [Fact]
        public void IsActiveIsTrueWhenValueStopwatchStartedWithStartNew()
        {
            Assert.True(ValueStopwatch.StartNew().IsActive);
        }

        [Fact]
        public void GetElapsedTimeThrowsIfValueStopwatchIsDefaultValue()
        {
            var stopwatch = default(ValueStopwatch);
            Assert.Throws<InvalidOperationException>(() => stopwatch.GetElapsedTime());
        }

        [Fact]
        public async Task GetElapsedTimeReturnsTimeElapsedSinceStart()
        {
            var stopwatch = ValueStopwatch.StartNew();
            await Task.Delay(200);
            Assert.True(stopwatch.GetElapsedTime().TotalMilliseconds > 0);
        }

        [Fact]
        public void RemainsActiveAfterDuration()
        {
            var stopwatch = ValueStopwatch.StartNew();
            _ = stopwatch.GetElapsedTime();
            Assert.True(stopwatch.IsActive);
        }
    }
}
