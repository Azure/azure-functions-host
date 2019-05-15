// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SlidingWindowTests
    {
        [Fact]
        public async Task GetEvents_RemovesExpiredItems()
        {
            var expiry = TimeSpan.FromSeconds(1);
            var window = new SlidingWindow<MyItem>(expiry);
            var initialEventCount = 5;
            var insertionTimestamps = new DateTime[initialEventCount];

            for (int i = 0; i < initialEventCount; i++)
            {
                window.AddEvent(new MyItem { Data = i });
                insertionTimestamps[i] = DateTime.Now;
                await Task.Delay(100);
            }

            var evts = window.GetEvents().ToArray();

            // How many items have expired? We're at the mercy of the task scheduler here.
            // In theory we should have only waited 5 * 100 = 500ms and have zero expired items but we might have waited longer.
            var insertionCompleteTimestamp = DateTime.Now;
            var expiredCount = insertionTimestamps.Count(t => (insertionCompleteTimestamp - t) > expiry);
            var expectedCount = initialEventCount - expiredCount;

            // Check the total count and each of the non expired items
            Assert.Equal(expectedCount, evts.Length);
            for (int i = 0; i < expectedCount; i++)
            {
                Assert.Equal(i + expiredCount, evts[i].Data);
            }

            // now let the items expire
            await Task.Delay(expiry);

            // add a new event that shouldn't be expired
            var evt = new MyItem { Data = 7 };
            window.AddEvent(evt);

            evts = window.GetEvents().ToArray();
            Assert.Equal(1, evts.Length);
            Assert.Same(evt, evts[0]);
        }

        [Fact]
        public async Task AddEvent_RemovesExpiredItems()
        {
            var window = new SlidingWindow<MyItem>(TimeSpan.FromSeconds(1));
            StringBuilder log = new StringBuilder();
            for (int i = 0; i < 5; i++)
            {
                window.AddEvent(new MyItem { Data = i });
                log.AppendLine($"{DateTime.UtcNow.ToString("HH:mm:ss.FFFZ")}: Added item: {i}");
                Thread.Sleep(50);
            }

            var eventsField = window.GetType().GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic);
            var events = (List<SlidingWindow<MyItem>.Event>)eventsField.GetValue(window);
            int count = events.Count;
            Assert.True(count == 5, $"{DateTime.UtcNow} | Expected 5. Actual {count}.{Environment.NewLine}{log.ToString()}");

            // now let the items expire
            await Task.Delay(1000);

            // add a new event that shouldn't be expired
            var evt = new MyItem { Data = 8 };
            window.AddEvent(evt);

            Assert.Equal(1, events.Count);
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