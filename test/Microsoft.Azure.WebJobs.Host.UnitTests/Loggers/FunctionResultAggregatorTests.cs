// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class FunctionResultAggregatorTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(1000)]
        public async Task Aggregator_Flushes_WhenBatchIsFull(int batchSize)
        {
            int publishCalls = 0;

            ILoggerFactory factory = CreateMockLoggerFactory((props) =>
            {
                publishCalls++;
                Assert.Equal(batchSize, props[LoggingKeys.Successes]);
            });

            var aggregator = new FunctionResultAggregator(batchSize, TimeSpan.FromMinutes(1), factory);

            await AddSuccessResults(aggregator, batchSize * 5);

            // allow a flush
            await Task.Delay(100);

            Assert.Equal((batchSize * 5) / batchSize, publishCalls);
        }

        [Fact]
        public async Task Aggregator_Flushes_WhenTimerFires()
        {
            int batchSize = 1000;
            int numberToInsert = 10;
            int publishCalls = 0;

            ILoggerFactory factory = CreateMockLoggerFactory((props) =>
            {
                publishCalls++;
                Assert.Equal(numberToInsert, props[LoggingKeys.Successes]);
            });

            var aggregator = new FunctionResultAggregator(batchSize, TimeSpan.FromSeconds(1), factory);

            await AddSuccessResults(aggregator, numberToInsert);

            // allow the timer to fire
            await Task.Delay(1100);

            Assert.Equal(1, publishCalls);
        }

        [Fact]
        public async Task Aggregator_AlternatesTimerAndBatch()
        {
            int publishCalls = 0;
            int totalSuccesses = 0;
            int batchSize = 100;

            ILoggerFactory factory = CreateMockLoggerFactory((props) =>
            {
                publishCalls++;
                totalSuccesses += Convert.ToInt32(props[LoggingKeys.Successes]);
            });

            var aggregator = new FunctionResultAggregator(batchSize, TimeSpan.FromSeconds(1), factory);

            // do this loop twice to ensure it continues working
            for (int i = 0; i < 2; i++)
            {
                // insert 225. Expect 2 calls to publish, then a flush for 25.
                await AddSuccessResults(aggregator, 225);

                await Task.Delay(1100);
                Assert.Equal(3 * (i + 1), publishCalls);
            }

            Assert.Equal(6, publishCalls);
            Assert.Equal(450, totalSuccesses);
        }

        [Fact]
        public async Task Aggregator_FlushesOnCancelation()
        {
            int batchSize = 100;
            int publishCalls = 0;

            ILoggerFactory factory = CreateMockLoggerFactory((props) =>
            {
                publishCalls++;
                Assert.Equal(10, props[LoggingKeys.Successes]);
            });

            var aggregator = new FunctionResultAggregator(batchSize, TimeSpan.FromSeconds(1), factory);

            await AddSuccessResults(aggregator, 10);
        }

        [Fact]
        public async Task Aggregator_Trims_DefaultClassName()
        {
            int batchSize = 10;
            int publishCalls = 0;

            ILoggerFactory factory = CreateMockLoggerFactory((props) =>
            {
                publishCalls++;
                Assert.Equal(10, props[LoggingKeys.Successes]);
                Assert.Equal("SomeTest", props[LoggingKeys.Name]);
            });

            var aggregator = new FunctionResultAggregator(batchSize, TimeSpan.FromSeconds(1), factory);

            await AddSuccessResults(aggregator, 10, "Functions.SomeTest");
        }

        [Fact]
        public async Task Aggregator_DoesNotTrim_NonDefaultClassName()
        {
            int batchSize = 10;
            int publishCalls = 0;

            ILoggerFactory factory = CreateMockLoggerFactory((props) =>
            {
                publishCalls++;
                Assert.Equal(10, props[LoggingKeys.Successes]);
                Assert.Equal("AnotherClass.SomeTest", props[LoggingKeys.Name]);
            });

            var aggregator = new FunctionResultAggregator(batchSize, TimeSpan.FromSeconds(1), factory);

            await AddSuccessResults(aggregator, 10, "AnotherClass.SomeTest");
        }


        private static async Task AddSuccessResults(IAsyncCollector<FunctionInstanceLogEntry> aggregator, int count, string functionName = null)
        {
            // Simulate the real executor, where a "function start" and "function end" are both added.
            // The aggregator will filter out any with EndTime == null
            for (int i = 0; i < count; i++)
            {
                await aggregator.AddAsync(new FunctionInstanceLogEntry
                {
                    FunctionName = functionName,
                    ErrorDetails = null,
                    EndTime = null
                });

                await aggregator.AddAsync(new FunctionInstanceLogEntry
                {
                    FunctionName = functionName,
                    ErrorDetails = null,
                    EndTime = DateTime.Now
                });
            }
        }

        private static ILoggerFactory CreateMockLoggerFactory(Action<IDictionary<string, object>> logAction)
        {
            ILogger logger = new MockLogger(logAction);

            Mock<ILoggerFactory> mockFactory = new Mock<ILoggerFactory>();
            mockFactory
                .Setup(lf => lf.CreateLogger(It.IsAny<string>()))
                .Returns(logger);

            return mockFactory.Object;
        }

        private class MockLogger : ILogger
        {
            private Action<IDictionary<string, object>> _logAction;

            public MockLogger(Action<IDictionary<string, object>> logAction)
            {
                _logAction = logAction;
            }
            public IDisposable BeginScope<TState>(TState state)
            {
                throw new NotImplementedException();
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                // in these tests, we will only ever see Information;
                Assert.Equal(LogLevel.Information, logLevel);
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                IEnumerable<KeyValuePair<string, object>> props = state as IEnumerable<KeyValuePair<string, object>>;
                Assert.NotNull(props);

                _logAction(props.ToDictionary(k => k.Key, k => k.Value));
            }
        }
    }
}
