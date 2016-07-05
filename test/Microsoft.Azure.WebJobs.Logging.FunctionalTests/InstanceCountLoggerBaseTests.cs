// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Xunit;

namespace Microsoft.Azure.WebJobs.Logging.Internal.FunctionalTests
{
    // Test function-instance count logger. 
    // Pure in-memory tests (mock out azure storage and timer). 
    public class InstanceCountLoggerBaseTests
    {
        // Activity within a single polling interval. 
        [Fact]
        public async Task BasicAndStop()
        {
            TestLogger l = new TestLogger();

            Guid g1 = Guid.NewGuid();

            // Activity within single poll interval. 
            l.Increment(g1); // will start the timer. 
            l.Increment(g1); // ignored, already started g1.                         
            l.Decrement(g1); // count is back to 0 before poll happens. 
            
            Assert.Equal(0, l._dict.Count); // Large poll, haven't written yet. 

            l._newTicks = 100;
            await l.StopAsync();

            // StopAsync will flush and cause a write. 
            // We still picked up g1
            Assert.Equal(1, l._dict.Count);
            Assert.Equal(1, l._dict[100]);

            Assert.Equal(1, l._totalActive); // duplicate g1 is only counted once
        }

        // Activity within a single polling interval. 
        [Fact]
        public async Task BasicAndPoll()
        {
            TestLogger l = new TestLogger();

            Guid g1 = Guid.NewGuid();

            // Activity within single poll interval. 
            l.Increment(g1); // will start the timer. 
            l.Decrement(g1); // ignored, already staretd g1.                         
            
            Assert.Equal(0, l._dict.Count); // Large poll, haven't written yet. 

            l.Poll(100);
            l._newTicks = 200;
            await l.StopAsync(); // no extra counts, 

            // StopAsync will flush and cause a write. 
            // We still picked up g1
            Assert.Equal(1, l._dict.Count);
            Assert.Equal(1, l._dict[100]);

            Assert.Equal(1, l._totalActive);
        }

        [Fact]
        public async Task StopWithNoActivity()
        {
            TestLogger l = new TestLogger();
            Guid g1 = Guid.NewGuid();

            await l.StopAsync();
            Assert.Equal(0, l._dict.Count);
        }

        // It's ok to call Stop() many times. 
        [Fact]
        public async Task MultiStop()
        {
            TestLogger l = new TestLogger();
            Guid g1 = Guid.NewGuid();

            l.Increment(g1);

            l._newTicks = 101;

            Task[] tasks = Array.ConvertAll(Enumerable.Range(1, 10).ToArray(), _ => l.StopAsync());
            await Task.WhenAll(tasks);

            Assert.Equal(1, l._totalActive);
            Assert.Equal(1, l._dict.Count);
            Assert.Equal(1, l._dict[101]);
        }

        [Fact]
        public async Task Consistency()
        {
            // Simulate a steady stream over a long time.
            TestLogger l = new TestLogger();

            for (int i = 0; i < 1000; i += 100)
            {
                var g = Guid.NewGuid();
                l.Increment(g);
                l.Decrement(g);
                l.Poll(i);
            }
            l._newTicks = 1000;
            await l.StopAsync();

            // Verify 
            int c = 0;
            for (int i = 0; i < 1000; i += 100)
            {
                Assert.Equal(1, l._dict[i]);
                c++;
            }
            Assert.Equal(c, l._totalActive);
            Assert.Equal(c, l._dict.Count);
        }

        [Fact]
        public async Task StopAndRestart()
        {
            TestLogger l = new TestLogger();
            Guid g1 = Guid.NewGuid();
            Guid g2 = Guid.NewGuid();
            Guid g3 = Guid.NewGuid();

            l.Increment(g1);
            l.Increment(g2);
            l.Decrement(g1);

            l._newTicks = 100;
            await l.StopAsync();

            Assert.Equal(2, l._dict[100]); // max was g1,g2

            l.Increment(g3); // start up again 
            l.Poll(200);
            l.Decrement(g2);
            l.Decrement(g3);

            l.Poll(300);

            l._newTicks = 301;
            await l.StopAsync();

            
            Assert.Equal(2, l._dict[200]); // max was g2, g3

            Assert.Equal(3, l._totalActive);

            Assert.Equal(2, l._dict[300]); // spill over from time-200
            Assert.Equal(3, l._dict.Count); 
        }

        // 1 long instance,  across multiple polls;. 
        [Fact]
        public void LongInstance()
        {
            TestLogger l = new TestLogger();
            Guid g1 = Guid.NewGuid();

            l.Increment(g1);
            l.Poll(10);
            l.Poll(20);
            l.Poll(30);
            l.Decrement(g1);
            l.Poll(40); // this will catch partial activity after 30
            l.Poll(41); // should be 0, so no entry 

            Assert.Equal(1, l._totalActive);

            Assert.Equal(4, l._dict.Count);
            Assert.Equal(1, l._dict[10]);
            Assert.Equal(1, l._dict[20]);
            Assert.Equal(1, l._dict[30]);

            // This is 1, not 0, because it's catching the partial activity between 30 and 40. 
            // But no entry after Time40. 
            Assert.Equal(1, l._dict[40]);
        }

        // Pure in-memory logger. 
        public class TestLogger : InstanceCountLoggerBase
        {
            // Record WriteEntry results. Map Ticks --> value at that time. 
            public Dictionary<long, int> _dict = new Dictionary<long, int>();

            // Events to handshake between Poll() from test thread and WriteEntry() on background thread.
            private readonly AutoResetEvent _eventPollReady = new AutoResetEvent(false);
            private readonly AutoResetEvent _eventFinishedWrite = new AutoResetEvent(false);
            public long _newTicks;

            public int _totalActive;

            public TestLogger()
            {                
            }

            // Callback from background Poller thread. 
            protected override Task WriteEntry(long ticks, int currentActive, int totalThisPeriod)
            {
                if (currentActive > 0)
                {
                    _dict.Add(ticks, currentActive); // shouldn't exist yet. 
                }
                _totalActive += totalThisPeriod;

                _eventFinishedWrite.Set(); // unblocks call to Poll() on main thread. 

                return Task.FromResult(0);
            }
                       

            // Callback from poller thread. 
            protected override async Task<long> WaitOnPoll(CancellationToken token)
            {
                await WaitOne(_eventPollReady, token);

                return _newTicks;
                // Thread returns and calls WriteEntry()             
            }

            // Test method called by test main thread. 
            // Signals WaitOnPoll to continue. Will block until WriteEntry() has been called. 
            public void Poll(long newTicks)
            {
                _eventFinishedWrite.Reset();

                _newTicks = newTicks;
                _eventPollReady.Set();

                // Block until Write has finished 
                WaitOne(_eventFinishedWrite, CancellationToken.None).Wait();
            }

            // Helper for waiting on an event. 
            static async Task WaitOne(AutoResetEvent e, CancellationToken token)
            {
                await Task.Yield();

                while (!token.IsCancellationRequested)
                {
                    if (e.WaitOne(0))
                    {
                        return;
                    }
                    await Task.Delay(10);
                }
            }
        }        
    }    
}