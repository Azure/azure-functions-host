// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        public async Task NoForwardingProviders_DrainsSourceAndLeavesItEnabled()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();
            source.Write(CreateErrorEntry("TestCategory", "Error Log"));

            // With no forwarding providers the reader still runs and drains (discards) buffered entries so
            // they don't accumulate, without disabling the shared buffer.
            var service = new DeferredLogForwardingService(source, Array.Empty<ILoggerProvider>(), CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);

            Assert.NotNull(service.ProcessingTask);
            await TestHelpers.Await(() => source.Reader.Count == 0);
            Assert.True(source.IsEnabled);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task NoAiOrOtelProviders_DrainsViaNoOpProvider()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();
            source.Write(CreateErrorEntry("TestCategory", "Error Log"));

            // A non-AI/OTel provider is filtered out and a no-op provider is substituted, so the buffer is
            // still drained but the passed-in provider must not receive the forwarded logs.
            var testLoggerProvider = new TestLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { testLoggerProvider },
                Options.Create(CreateOptions(isStandby: false)), environment);

            await service.StartAsync(CancellationToken.None);

            Assert.NotNull(service.ProcessingTask);
            await TestHelpers.Await(() => source.Reader.Count == 0);
            Assert.True(source.IsEnabled);
            Assert.Empty(testLoggerProvider.GetAllLogMessages());

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

        [Fact]
        public async Task ForwardingContinues_WhenProviderCreateLoggerThrows()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();
            source.Write(CreateErrorEntry("TestCategory", "Error Log"));

            // The first provider throws from CreateLogger; forwarding must still reach the healthy provider and
            // must not terminate the processing loop (which would silently stop all future forwarding).
            var healthyProvider = new TestLoggerProvider();
            var service = new DeferredLogForwardingService(source,
                new ILoggerProvider[] { new ThrowingLoggerProvider(), healthyProvider },
                CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() => healthyProvider.GetAllLogMessages().Any());
            Assert.Equal("Error Log", healthyProvider.GetAllLogMessages().Single().FormattedMessage);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task ForwardsScopes_WhenEntryHasScopeStorage()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();

            // An entry carrying captured scopes must have them reapplied when forwarded so the provider
            // observes them (exercises ProcessLogWithScope).
            source.Write(new DeferredLogEntry
            {
                LogLevel = LogLevel.Error,
                Category = "TestCategory",
                Message = "Error Log",
                ScopeStorage = new List<object>
                {
                    new Dictionary<string, object> { ["ScopeKey"] = "ScopeValue" }
                }
            });

            var testLoggerProvider = new TestLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { testLoggerProvider },
                CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);
            await TestHelpers.Await(() => testLoggerProvider.GetAllLogMessages().Any());

            LogMessage message = testLoggerProvider.GetAllLogMessages().Single();
            Assert.Equal("Error Log", message.FormattedMessage);
            Assert.NotNull(message.Scope);
            Assert.Equal("ScopeValue", message.Scope["ScopeKey"]);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task ForwardsNestedScopes_ReappliedOuterToInner()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();

            // Two stacked scopes must be reapplied in the captured outer -> inner order, then unwound in reverse.
            source.Write(new DeferredLogEntry
            {
                LogLevel = LogLevel.Error,
                Category = "TestCategory",
                Message = "Error Log",
                ScopeStorage = new List<object> { "outer", "inner" }
            });

            var recordingProvider = new ScopeRecordingLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { recordingProvider },
                CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);
            await TestHelpers.Await(() => recordingProvider.Logger.ScopesAtLog is not null);

            Assert.Equal(new[] { "outer", "inner" }, recordingProvider.Logger.ScopesAtLog);
            Assert.Empty(recordingProvider.Logger.ActiveScopes);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        [Fact]
        public async Task MultipleForwarders_SharingSource_ForwardEachEntryOnce()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();

            const int count = 50;
            for (int i = 0; i < count; i++)
            {
                source.Write(CreateErrorEntry("TestCategory", $"Log {i}"));
            }

            // The shared buffer sets SingleReader=false to tolerate overlapping ScriptHosts during restart.
            var provider1 = new TestLoggerProvider();
            var provider2 = new TestLoggerProvider();
            var service1 = new DeferredLogForwardingService(source, new ILoggerProvider[] { provider1 }, CreateOptions(isStandby: false), environment);
            var service2 = new DeferredLogForwardingService(source, new ILoggerProvider[] { provider2 }, CreateOptions(isStandby: false), environment);

            await service1.StartAsync(CancellationToken.None);
            await service2.StartAsync(CancellationToken.None);

            // Complete the shared channel so both readers drain what's buffered and exit.
            source.Disable();
            await service1.ProcessingTask;
            await service2.ProcessingTask;

            var forwarded = provider1.GetAllLogMessages().Concat(provider2.GetAllLogMessages())
                .Select(m => m.FormattedMessage).ToList();

            // Two concurrent readers on one channel: each entry is delivered to exactly one reader (no loss, no duplication).
            Assert.Equal(count, forwarded.Count);
            Assert.Equal(count, forwarded.Distinct().Count());

            service1.Dispose();
            service2.Dispose();
        }

        [Fact]
        public async Task StopAsync_HonorsCancellationToken_WhenProcessingIsBlocked()
        {
            var environment = new TestEnvironment();
            var source = new DeferredLogSource();
            source.Write(CreateErrorEntry("TestCategory", "Error Log"));

            // Block the processing loop inside the provider's Log so it cannot complete on its own.
            var blockingProvider = new BlockingLoggerProvider();
            var service = new DeferredLogForwardingService(source, new ILoggerProvider[] { blockingProvider },
                CreateOptions(isStandby: false), environment);

            await service.StartAsync(CancellationToken.None);
            Assert.True(blockingProvider.LogEntered.Wait(TimeSpan.FromSeconds(30)));

            // With an already-cancelled shutdown token, StopAsync must return promptly rather than waiting for
            // the (blocked) processing task.
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await service.StopAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(30));

            blockingProvider.Release();
            await service.ProcessingTask.WaitAsync(TimeSpan.FromSeconds(30));
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

        private sealed class ThrowingLoggerProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => throw new InvalidOperationException("Simulated CreateLogger failure.");

            public void Dispose()
            {
            }
        }

        private sealed class BlockingLoggerProvider : ILoggerProvider
        {
            private readonly ManualResetEventSlim _release = new(false);

            public ManualResetEventSlim LogEntered { get; } = new(false);

            public ILogger CreateLogger(string categoryName) => new BlockingLogger(this);

            public void Release() => _release.Set();

            public void Dispose()
            {
                _release.Dispose();
                LogEntered.Dispose();
            }

            private sealed class BlockingLogger : ILogger
            {
                private readonly BlockingLoggerProvider _provider;

                public BlockingLogger(BlockingLoggerProvider provider) => _provider = provider;

                public IDisposable BeginScope<TState>(TState state) => NullLogger.Instance.BeginScope(state);

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
                {
                    _provider.LogEntered.Set();
                    _provider._release.Wait();
                }
            }
        }

        private sealed class ScopeRecordingLoggerProvider : ILoggerProvider
        {
            public ScopeRecordingLogger Logger { get; } = new ScopeRecordingLogger();

            public ILogger CreateLogger(string categoryName) => Logger;

            public void Dispose()
            {
            }
        }

        private sealed class ScopeRecordingLogger : ILogger
        {
            public List<object> ActiveScopes { get; } = new List<object>();

            public string[] ScopesAtLog { get; private set; }

            public IDisposable BeginScope<TState>(TState state)
            {
                ActiveScopes.Add(state);
                return new ScopePopper(ActiveScopes);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                // Snapshot the active scopes (outer -> inner) at the moment the entry is written.
                ScopesAtLog = ActiveScopes.Select(scope => scope?.ToString()).ToArray();
            }

            private sealed class ScopePopper : IDisposable
            {
                private readonly List<object> _scopes;

                public ScopePopper(List<object> scopes) => _scopes = scopes;

                public void Dispose() => _scopes.RemoveAt(_scopes.Count - 1);
            }
        }
    }
}
