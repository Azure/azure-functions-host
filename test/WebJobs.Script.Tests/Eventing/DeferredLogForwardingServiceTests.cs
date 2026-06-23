// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DeferredLogForwardingServiceTests
    {
        [Fact]
        public async Task ForwardsBufferedLogs_ToForwardingProviders()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();
            source.Write(CreateErrorEntry("TestCategory", "Error Log"));

            var testLoggerProvider = new TestLoggerProvider();
            testLoggerProvider.SetScopeProvider(new LoggerExternalScopeProvider());

            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { testLoggerProvider }, CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);

            // Complete the channel so the processing loop drains and exits.
            source.Disable();
            await service.ProcessingTask;

            var message = Assert.Single(testLoggerProvider.GetAllLogMessages());
            Assert.Equal("Error Log", message.FormattedMessage);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task DoesNotForward_InStandbyConfiguration()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();

            var testLoggerProvider = new TestLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { testLoggerProvider }, CreateOptions(isStandby: true), environment);

            await service.StartAsync(CancellationToken.None);

            Assert.Null(service.ProcessingTask);

            source.Write(CreateErrorEntry("TestCategory", "Error Log"));
            Assert.Empty(testLoggerProvider.GetAllLogMessages());
            Assert.Equal(1, source.Reader.Count);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task DoesNotForward_WhenForwardingDisabledByFeatureFlag()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagDisableWebHostLogForwarding);
            var source = new DeferredLogSource();

            var testLoggerProvider = new TestLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { testLoggerProvider }, CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);

            Assert.Null(service.ProcessingTask);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task NoForwardingProviders_DoesNotStartReaderOrDisableSource()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();
            source.Write(CreateErrorEntry("TestCategory", "Error Log"));

            var service = new DeferredLogForwardingService(source, Array.Empty<ILoggerProvider>(), CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);

            // No reader is started, and the shared buffer is left intact (not disabled, not drained) so a
            // later host that does have providers can still forward the buffered logs.
            Assert.Null(service.ProcessingTask);
            Assert.True(source.IsEnabled);
            Assert.Equal(1, source.Reader.Count);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task StopAsync_StopsForwarding()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();

            var testLoggerProvider = new TestLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { testLoggerProvider }, CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);

            // The service is parked waiting for logs; StopAsync should cancel and complete it.
            await service.StopAsync(CancellationToken.None);

            Assert.True(service.ProcessingTask.IsCompleted);
            service.Dispose();
        }

        private static ScriptApplicationHostOptions CreateOptions(bool isStandby)
        {
            return new ScriptApplicationHostOptions { IsStandbyConfiguration = isStandby };
        }

        private static DeferredLogEntry CreateErrorEntry(string category, string message)
        {
            return new DeferredLogEntry
            {
                LogLevel = LogLevel.Error,
                Category = category,
                Message = message
            };
        }
    }
}
