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
    /// ScriptHost and is stopped by the base <see cref="BackgroundService"/> when the host is orphaned
    /// (restart or specialization). When the host has no Application Insights / OpenTelemetry providers, a no-op
    /// <see cref="NullLoggerProvider"/> is used so the shared buffer is still drained (and the entries
    /// discarded) rather than left to accumulate with no consumer.
    /// </summary>
    internal sealed class DeferredLogForwardingService : BackgroundService
    {
        private readonly DeferredLogSource _source;
        private readonly IReadOnlyList<ILoggerProvider> _forwardingProviders;
        private readonly Dictionary<string, ILogger[]> _loggersByCategory = new(StringComparer.Ordinal);
        private readonly ScriptApplicationHostOptions _options;
        private readonly IEnvironment _environment;

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

        // Exposes the base-class execution task for tests. Remains null when forwarding is skipped in
        // StartAsync (standby/feature-flag), because ExecuteAsync is never started in that case.
        internal Task ProcessingTask => ExecuteTask;

        public override Task StartAsync(CancellationToken cancellationToken)
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
                // Skip base.StartAsync so ExecuteAsync (and ExecuteTask) is never started.
                return Task.CompletedTask;
            }

            // The base BackgroundService owns the lifetime CancellationTokenSource: it starts ExecuteAsync
            // now and cancels that token from StopAsync/Dispose when this host is orphaned.
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yield so draining any already-buffered logs doesn't run inline on the ScriptHost startup path.
            await Task.Yield();

            try
            {
                ChannelReader<DeferredLogEntry> reader = _source.Reader;
                while (await reader.WaitToReadAsync(stoppingToken))
                {
                    while (!stoppingToken.IsCancellationRequested && reader.TryRead(out DeferredLogEntry log))
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
    }
}
