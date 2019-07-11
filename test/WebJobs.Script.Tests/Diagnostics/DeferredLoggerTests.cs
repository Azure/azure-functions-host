// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    // Tests for flowing logs from WebHost to JobHot.
    // Flow is DeferredLogger -> DeferredLogSource -> DeferredLogService
    public class DeferredLoggerTests
    {
        [Fact]
        public async Task DeferredLogger_DropsMessages_WhenBufferFull()
        {
            var provider = new DeferredLoggerProvider(3);
            provider.SetScopeProvider(new LoggerExternalScopeProvider());

            var logger = provider.CreateLogger("TestCategory");

            // Buffer is 3. Send a few extra messages and make sure it doesn't throw or block.
            for (int i = 1; i <= 10; i++)
            {
                logger.LogInformation(i.ToString());
            }

            // Completes the flow
            provider.Dispose();

            ChannelReader<DeferredLogMessage> buffer = provider.LogChannel;

            // Now make sure we receive those 3 that were buffered.
            int count = 0;
            while (await buffer.WaitToReadAsync())
            {
                var message = await buffer.ReadAsync();
                Assert.Equal((++count).ToString(), message.State.ToString());
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task Logs_AreBufferedAndFlushedAsync()
        {
            var factory = new LoggerFactory();
            var provider = new TestLoggerProvider();
            factory.AddProvider(provider);

            var logSource = new DeferredLoggerProvider();
            logSource.SetScopeProvider(new LoggerExternalScopeProvider());
            var logger1 = logSource.CreateLogger("TestCategory1");
            var logger2 = logSource.CreateLogger("TestCategory2");

            // Add 10 messages to each logger before instantiating the the service, which will flush them
            for (int i = 0; i < 10; i++)
            {
                logger1.LogInformation(i.ToString());
                logger2.LogInformation(i.ToString());
            }

            Assert.Empty(provider.GetAllLogMessages());

            var service = new DeferredLoggerService(logSource, factory);
            await service.StartAsync(CancellationToken.None);

            // Make sure they're all processed
            await TestHelpers.Await(() => provider.GetAllLogMessages().Count == 20);

            // Now log three more.
            for (int i = 10; i < 13; i++)
            {
                logger1.LogInformation(i.ToString());
                logger2.LogInformation(i.ToString());
            }

            await TestHelpers.Await(() => provider.GetAllLogMessages().Count == 26);

            await service.StopAsync(CancellationToken.None);

            // Log 5 more while the service is stopped.
            for (int i = 13; i < 18; i++)
            {
                logger1.LogInformation(i.ToString());
                logger2.LogInformation(i.ToString());
            }

            // Re-instantiate the service and make sure it flushes again
            service = new DeferredLoggerService(logSource, factory);
            await service.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() => provider.GetAllLogMessages().Count == 36);

            await service.StopAsync(CancellationToken.None);

            var messages = provider.GetAllLogMessages();
            for (int i = 0; i < 36; i++)
            {
                Assert.Equal((i / 2).ToString(), messages[i].FormattedMessage);
            }
        }

        [Fact]
        public async Task Scope_IsReapplied()
        {
            var factory = new LoggerFactory();
            var provider = new TestLoggerProvider();
            factory.AddProvider(provider);

            var logSource = new DeferredLoggerProvider();
            logSource.SetScopeProvider(new LoggerExternalScopeProvider());
            var logger = logSource.CreateLogger("TestCategory1");

            using (logger.BeginScope(CreateScopeDictionary("1", "1")))
            {
                using (logger.BeginScope(CreateScopeDictionary("2", "2")))
                {
                    using (logger.BeginScope(CreateScopeDictionary("3", "3")))
                    {
                        using (logger.BeginScope(CreateScopeDictionary("2", "override")))
                        {
                            logger.LogInformation("Log 1");
                        }
                        logger.LogInformation("Log 2");
                    }
                    logger.LogInformation("Log 3");
                }
            }

            Assert.Empty(provider.GetAllLogMessages());

            var service = new DeferredLoggerService(logSource, factory);
            await service.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() => provider.GetAllLogMessages().Count == 3);

            LogMessage log1 = provider.GetAllLogMessages().First();
            Assert.Equal("Log 1", log1.FormattedMessage);
            Assert.Collection(log1.Scope,
                p => Assert.True(p.Key == "1" && p.Value.ToString() == "1"),
                p => Assert.True(p.Key == "2" && p.Value.ToString() == "override"),
                p => Assert.True(p.Key == "3" && p.Value.ToString() == "3"),
                p => Assert.True(p.Key == "MS_DeferredLog" && (bool)p.Value));

            LogMessage log2 = provider.GetAllLogMessages().ElementAt(1);
            Assert.Collection(log2.Scope,
                p => Assert.True(p.Key == "1" && p.Value.ToString() == "1"),
                p => Assert.True(p.Key == "2" && p.Value.ToString() == "2"),
                p => Assert.True(p.Key == "3" && p.Value.ToString() == "3"),
                p => Assert.True(p.Key == "MS_DeferredLog" && (bool)p.Value));

            LogMessage log3 = provider.GetAllLogMessages().Last();
            Assert.Collection(log3.Scope,
                p => Assert.True(p.Key == "1" && p.Value.ToString() == "1"),
                p => Assert.True(p.Key == "2" && p.Value.ToString() == "2"),
                p => Assert.True(p.Key == "MS_DeferredLog" && (bool)p.Value));
        }

        private IDictionary<string, object> CreateScopeDictionary(string key, object value)
        {
            return new Dictionary<string, object>
            {
                { key, value }
            };
        }
    }
}