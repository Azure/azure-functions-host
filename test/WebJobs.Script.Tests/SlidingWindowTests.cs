// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SlidingWindowTests
    {
        [Fact]
        public async Task GetEvents_RemovesExpiredItems()
        {
            var window = new SlidingWindow<MyItem>(TimeSpan.FromSeconds(1));

            for (int i = 0; i < 5; i++)
            {
                window.AddEvent(new MyItem { Data = i });
                await Task.Delay(100);
            }

            var evts = window.GetEvents().ToArray();
            Assert.Equal(5, evts.Length);
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(i, evts[i].Data);
            }

            // now let the items expire
            await Task.Delay(1000);

            // add a new event that shouldn't be expired
            var evt = new MyItem { Data = 7 };
            window.AddEvent(evt);

            evts = window.GetEvents().ToArray();
            Assert.Equal(1, evts.Length);
            Assert.Same(evt, evts[0]);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(3, false)]
        [InlineData(14, false)]
        [InlineData(16, true)]
        [InlineData(20, true)]
        public void IsExpired_ReturnsExpectedValue(int t, bool expected)
        {
            var window = TimeSpan.FromMinutes(15);
            var evt = new SlidingWindow<MyItem>.Event();

            evt.TimeStamp = (DateTime.UtcNow - TimeSpan.FromMinutes(t)).Ticks;
            Assert.Equal(expected, SlidingWindow<MyItem>.IsExpired(evt, window));
        }

        private class MyItem
        {
            public int Data { get; set; }
        }
    }
}
