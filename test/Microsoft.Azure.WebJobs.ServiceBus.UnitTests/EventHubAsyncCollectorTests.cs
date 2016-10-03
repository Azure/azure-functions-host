// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.ServiceBus.UnitTests
{
    public class EventHubAsyncCollectorTests
    {
        public class TestEventHubAsyncCollector : EventHubAsyncCollector
        {
            // A fake connection string for event hubs. This just needs to parse. It won't actually get invoked. 
            const string FakeConnectionString = "Endpoint=sb://test89123-ns-x.servicebus.windows.net/;SharedAccessKeyName=ReceiveRule;SharedAccessKey=secretkey;EntityPath=path2";
            public static EventHubClient _testClient = EventHubClient.CreateFromConnectionString(FakeConnectionString);

            // EventData is disposed after sending. So track raw bytes; not the actual EventData. 
            public List<byte[]> _sentEvents = new List<byte[]>();

            public TestEventHubAsyncCollector() : base(_testClient)
            {
            }
            protected override Task SendBatchAsync(EventData[] batch)
            {
                lock(_sentEvents)
                {
                    foreach (var e in batch)
                    {
                        var payloadBytes = e.GetBytes();
                        Assert.NotNull(payloadBytes);
                        _sentEvents.Add(payloadBytes);
                    }
                }
                return Task.FromResult(0);
            }
        }

        [Fact]
        public void NullArgumentCheck()
        {
            Assert.Throws<ArgumentNullException>(() => new EventHubAsyncCollector(null));
        }             

        [Fact]
        public async Task NotSentUntilFlushed()
        {
            var collector = new TestEventHubAsyncCollector();

            await collector.FlushAsync(); // should be nop. 

            var payload = new byte[] { 1, 2, 3 };
            var e1 = new EventData(payload);
            await collector.AddAsync(e1);

            // Not physically sent yet since we haven't flushed 
            Assert.Equal(0, collector._sentEvents.Count);

            await collector.FlushAsync();
            Assert.Equal(1, collector._sentEvents.Count);
            Assert.Equal(payload, collector._sentEvents[0]);

            // Verify the event was disposed.
            Assert.Throws<ObjectDisposedException>(() => e1.GetBodyStream());
        }

        // If we send enough events, that will eventually tip over and flush. 
        [Fact]
        public async Task FlushAfterLotsOfSmallEvents()
        {
            var collector = new TestEventHubAsyncCollector();

            // Sending a bunch of little events 
            for (int i = 0; i < 150; i++)
            {
                var e1 = new EventData(new byte[] { 1, 2, 3 });
                await collector.AddAsync(e1);
            }

            Assert.True(collector._sentEvents.Count > 0);
        }

        // If we send enough events, that will eventually tip over and flush. 
        [Fact]
        public async Task FlushAfterSizeThreshold()
        {
            var collector = new TestEventHubAsyncCollector();

            // Trip the 256k EventHub limit. 
            for (int i = 0; i < 10; i++)
            {
                var e1 = new EventData(new byte[10 * 1024]);
                await collector.AddAsync(e1);
            }
            // Not yet 
            Assert.Equal(0, collector._sentEvents.Count);

            // This will push it over the theshold
            for (int i = 0; i < 20; i++)
            {
                var e1 = new EventData(new byte[10 * 1024]);
                await collector.AddAsync(e1);
            }

            Assert.True(collector._sentEvents.Count > 0);
        }

        [Fact]
        public async Task CantSentGiantEvent()
        {
            var collector = new TestEventHubAsyncCollector();

            // event hub max is 256k payload. 
            var hugePayload = new byte[300 * 1024];
            var e1 = new EventData(hugePayload);

            try
            {
                await collector.AddAsync(e1);
                Assert.False(true);
            }
            catch (InvalidOperationException e)
            {
                // Exact error message (and serialization byte size) is subject to change. 
                Assert.Equal("Event is too large. Event is approximately 307208b and max size is 245760b", e.Message);
            }

            // Verify we didn't queue anything
            await collector.FlushAsync();
            Assert.Equal(0, collector._sentEvents.Count);
        }

        [Fact]
        public async Task CantSendNullEvent()
        {
            var collector = new TestEventHubAsyncCollector();

            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await collector.AddAsync(null)
            );
        }

        // Send lots of events from multiple threads and ensure that all events are precisely accounted for. 
        [Fact]
        public async Task SendLotsOfEvents()
        {
            var collector = new TestEventHubAsyncCollector();

            int numEvents = 1000;
            int numThreads = 10;

            HashSet<string> expected = new HashSet<string>();

            // Send from different physical threads.             
            Thread[] threads = new Thread[numThreads];
            for (int iThread = 0; iThread < numThreads; iThread++)
            {
                var x = iThread;
                threads[x] = new Thread(
                    () =>
                    {
                        for (int i = 0; i < numEvents; i++)
                        {
                            var idx = x * numEvents + i;
                            var payloadStr = idx.ToString();
                            var payload = Encoding.UTF8.GetBytes(payloadStr);
                            lock (expected)
                            {
                                expected.Add(payloadStr);
                            }
                            collector.AddAsync(new EventData(payload)).Wait();
                        }
                    });
            };


            foreach (var thread in threads)
            {
                thread.Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }


            // Add more events to trip flushing of the original batch without calling Flush()
            const string ignore = "ignore";
            byte[] ignoreBytes = Encoding.UTF8.GetBytes(ignore);
            for (int i = 0; i < 100; i++)
            {
                await collector.AddAsync(new EventData(ignoreBytes));
            }

            // Verify that every event we sent is accounted for; and that there are no duplicates. 
            int count = 0;
            foreach (var payloadBytes in collector._sentEvents)
            {
                count++;   
                var payloadStr = Encoding.UTF8.GetString(payloadBytes);
                if (payloadStr == ignore)
                {
                    continue;
                }
                if (!expected.Remove(payloadStr))
                {
                    // Already removed!
                    Assert.False(true, "event payload occured multiple times");
                }
            }

            Assert.Equal(0, expected.Count); // Some events where missed. 

        }            
    } // end class         
}
