// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
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
            var provider = new DeferredLoggerProvider(new ScriptWebHostEnvironment(new TestEnvironment()), 3);
            var logger = provider.CreateLogger("TestCategory");

            // Buffer is 3. Send a few extra messages and make sure it doesn't throw or block.
            for (int i = 1; i <= 10; i++)
            {
                logger.LogInformation(i.ToString());
            }

            // Completes the flow
            provider.Dispose();

            ISourceBlock<DeferredLogMessage> buffer = provider.LogBuffer;

            // Now make sure we receive those 3 that were buffered.
            int count = 0;
            while (await buffer.OutputAvailableAsync())
            {
                var message = await buffer.ReceiveAsync();
                Assert.Equal((++count).ToString(), message.State.ToString());
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task DeferredLogger_DoesNotLog_InStandbyMode()
        {
            bool standbyMode = true;
            var environment = new Mock<IScriptWebHostEnvironment>(MockBehavior.Strict);
            environment
                .SetupGet(m => m.InStandbyMode)
                .Returns(() => standbyMode);

            var provider = new DeferredLoggerProvider(environment.Object);
            var logger = provider.CreateLogger("TestCategory");

            for (int i = 0; i < 5; i++)
            {
                logger.LogInformation($"Standby {i}");
            }

            standbyMode = false;

            for (int i = 0; i < 5; i++)
            {
                logger.LogInformation($"Specialized {i}");
            }

            standbyMode = true;

            for (int i = 0; i < 5; i++)
            {
                logger.LogInformation($"Standby {i}");
            }

            provider.Dispose();

            int count = 0;
            while (await provider.LogBuffer.OutputAvailableAsync())
            {
                count++;
                var message = await provider.LogBuffer.ReceiveAsync();
                Assert.StartsWith("Specialized", message.State.ToString());
            }

            Assert.Equal(5, count);
        }

        [Fact]
        public async Task Logs_AreBufferedAndFlushedAsync()
        {
            var factory = new LoggerFactory();
            var provider = new TestLoggerProvider();
            factory.AddProvider(provider);

            var logSource = new DeferredLoggerProvider(new ScriptWebHostEnvironment());
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
    }
}
