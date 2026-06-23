// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// Reads buffered WebHost logs from the shared <see cref="DeferredLogSource"/> and forwards them to the
    /// active ScriptHost's Application Insights / OpenTelemetry providers. A new instance is created for each
    /// ScriptHost and is stopped via <see cref="StopAsync"/> when the host is orphaned (restart or
    /// specialization). During the default overlapping restart the previous host's forwarder can briefly read
    /// concurrently with the new one (hence <see cref="DeferredLogSource"/> permits multiple readers); the
    /// orphaned forwarder is awaited to completion before its providers are disposed. This replaces the
    /// imperative per-host-build forwarding that leaked accumulating readers across restarts.
    /// </summary>
    internal sealed class DeferredLogForwardingService : IHostedService, IDisposable
    {
        private readonly DeferredLogSource _source;
        private readonly IReadOnlyList<ILoggerProvider> _forwardingProviders;
        private readonly ScriptApplicationHostOptions _options;
        private readonly IEnvironment _environment;
        private readonly CancellationTokenSource _cts = new();
        private Task _processingTask;
        private bool _disposed;

        public DeferredLogForwardingService(DeferredLogSource source, IEnumerable<ILoggerProvider> loggerProviders,
            IOptions<ScriptApplicationHostOptions> options, IEnvironment environment)
            : this(source, FilterForwardingProviders(loggerProviders), options?.Value, environment)
        {
        }

        internal DeferredLogForwardingService(DeferredLogSource source, IReadOnlyList<ILoggerProvider> forwardingProviders,
            ScriptApplicationHostOptions options, IEnvironment environment)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _forwardingProviders = forwardingProviders ?? throw new ArgumentNullException(nameof(forwardingProviders));
        }

        internal Task ProcessingTask => _processingTask;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Don't forward logs in standby/placeholder mode. A placeholder host has no real (customer)
            // telemetry providers, so forwarding would target no-op providers and compete with the
            // specialized host for the same buffered logs. Only the active, specialized ScriptHost forwards.
            if (_options.IsStandbyConfiguration ||
                FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableWebHostLogForwarding, _environment))
            {
                return Task.CompletedTask;
            }

            // Nothing to forward to (no Application Insights / OpenTelemetry providers on this host). Don't
            // start a reader and don't disable the shared buffer: a later host (e.g. after specialization, or
            // a restart that adds providers) may still consume it. The buffer is bounded, so it self-limits.
            if (_forwardingProviders.Count == 0)
            {
                return Task.CompletedTask;
            }

            // Offload to the thread pool so draining any already-buffered logs doesn't run inline on the
            // ScriptHost startup path.
            _processingTask = Task.Run(() => ProcessLogsAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; nothing to cancel.
            }

            if (_processingTask is not null)
            {
                await _processingTask;
            }
        }

        private async Task ProcessLogsAsync(CancellationToken cancellationToken)
        {
            try
            {
                ChannelReader<DeferredLogEntry> reader = _source.Reader;
                while (await reader.WaitToReadAsync(cancellationToken))
                {
                    while (!cancellationToken.IsCancellationRequested && reader.TryRead(out DeferredLogEntry log))
                    {
                        foreach (ILoggerProvider forwardingProvider in _forwardingProviders)
                        {
                            try
                            {
                                ILogger logger = forwardingProvider.CreateLogger(log.Category);
                                if (log.ScopeStorage?.Count > 0)
                                {
                                    ProcessLogWithScope(logger, log);
                                }
                                else
                                {
                                    logger.Log(log.LogLevel, log.EventId, log.Exception, log.Message);
                                }
                            }
                            catch (Exception ex) when (!ex.IsFatal())
                            {
                                // A single misbehaving provider must not stop forwarding of subsequent logs.
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the ScriptHost is stopping or restarting.
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // Never let log forwarding bring down the host.
            }
        }

        private static void ProcessLogWithScope(ILogger logger, DeferredLogEntry log)
        {
            var scopes = new List<IDisposable>();
            try
            {
                // Create a scope for each object in ScopeStorage so they are reapplied in the original order.
                foreach (var scope in log.ScopeStorage)
                {
                    scopes.Add(logger.BeginScope(scope));
                }

                logger.Log(log.LogLevel, log.EventId, log.Exception, log.Message);
            }
            finally
            {
                // Dispose all scopes in reverse order to properly unwind them.
                for (int i = scopes.Count - 1; i >= 0; i--)
                {
                    scopes[i].Dispose();
                }
            }
        }

        // Forward only to the Application Insights and OpenTelemetry providers. They are added in the
        // ScriptHost and do not track these WebHost-level logs directly, so the deferred logs are forwarded
        // to them here; other providers (file, system, etc.) already capture WebHost logs.
        private static IReadOnlyList<ILoggerProvider> FilterForwardingProviders(IEnumerable<ILoggerProvider> loggerProviders)
        {
            return loggerProviders
                .Where(provider => provider is ApplicationInsightsLoggerProvider or OpenTelemetryLoggerProvider)
                .ToArray();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // Cancel before disposing so a still-running processing task (e.g. if StopAsync never ran
                // because host startup was aborted) doesn't stay parked on WaitToReadAsync.
                _cts.Cancel();
                _cts.Dispose();
            }
        }
    }
}
