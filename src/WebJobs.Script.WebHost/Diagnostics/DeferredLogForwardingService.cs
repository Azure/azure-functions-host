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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// Reads buffered WebHost logs from the shared <see cref="DeferredLogSource"/> and forwards them to the
    /// active ScriptHost's Application Insights / OpenTelemetry providers. A new instance is created for each
    /// ScriptHost and is stopped via <see cref="StopAsync"/> when the host is orphaned (restart or
    /// specialization). When the host has no Application Insights / OpenTelemetry providers, a no-op
    /// <see cref="NullLoggerProvider"/> is used so the shared buffer is still drained (and the entries
    /// discarded) rather than left to accumulate with no consumer.
    /// </summary>
    internal sealed class DeferredLogForwardingService : IHostedService, IDisposable
    {
        private readonly DeferredLogSource _source;
        private readonly IReadOnlyList<ILoggerProvider> _forwardingProviders;
        private readonly Dictionary<string, ILogger[]> _loggersByCategory = new(StringComparer.Ordinal);
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
            if (cancellationToken.IsCancellationRequested)
            {
                // Startup is being aborted; don't begin forwarding.
                return Task.FromCanceled(cancellationToken);
            }

            // Don't forward logs in standby/placeholder mode. A placeholder host has no real (customer)
            // telemetry providers, so forwarding would target no-op providers and compete with the
            // specialized host for the same buffered logs. Only the active, specialized ScriptHost forwards.
            if (_options.IsStandbyConfiguration ||
                FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableWebHostLogForwarding, _environment))
            {
                return Task.CompletedTask;
            }

            // Offload to the thread pool so draining any already-buffered logs doesn't run inline on the
            // ScriptHost startup path.
            CancellationToken token = _cts.Token;
            _processingTask = Task.Run(() => ProcessLogsAsync(token));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_processingTask is null)
            {
                return;
            }

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed; nothing to cancel.
            }

            // Wait for the processing loop to observe cancellation and exit, but honor the host's shutdown
            // timeout (cancellationToken) so a slow or stuck forward can't block graceful shutdown.
            await Task.WhenAny(_processingTask, Task.Delay(Timeout.Infinite, cancellationToken));
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
                        ILogger[] loggers = GetLoggers(log.Category);
                        foreach (ILogger logger in loggers)
                        {
                            try
                            {
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

        private ILogger[] GetLoggers(string category)
        {
            category ??= string.Empty;
            if (!_loggersByCategory.TryGetValue(category, out ILogger[] loggers))
            {
                loggers = new ILogger[_forwardingProviders.Count];
                for (int i = 0; i < _forwardingProviders.Count; i++)
                {
                    try
                    {
                        loggers[i] = _forwardingProviders[i].CreateLogger(category);
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        loggers[i] = NullLogger.Instance;
                    }
                }

                _loggersByCategory[category] = loggers;
            }

            return loggers;
        }

        // Forward only to the Application Insights and OpenTelemetry providers. They are added in the
        // ScriptHost and do not track these WebHost-level logs directly, so the deferred logs are forwarded
        // to them here; other providers (file, system, etc.) already capture WebHost logs.
        private static IReadOnlyList<ILoggerProvider> FilterForwardingProviders(IEnumerable<ILoggerProvider> loggerProviders)
        {
            ILoggerProvider[] providers = loggerProviders
                .Where(provider => provider is ApplicationInsightsLoggerProvider or OpenTelemetryLoggerProvider)
                .ToArray();

            // When the host has no Application Insights / OpenTelemetry providers (e.g. an app without
            // telemetry configured), forward to a no-op provider so the shared buffer is still continuously
            // drained rather than accumulating entries with no consumer.
            return providers.Length > 0
                ? providers
                : new ILoggerProvider[] { NullLoggerProvider.Instance };
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
