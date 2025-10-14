// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Service to manage health checks. This service supports both the WebHost and ScriptHost health checks.
    /// </summary>
    /// <remarks>
    /// The approach taken is to perform individual health checks on both the WebHost and ScriptHost, then merge the results.
    /// This is to ensure individual health checks run in the correct service scope (WebHost vs ScriptHost) from where they
    /// are registered.
    /// </remarks>
    public sealed partial class DynamicHealthCheckService : HealthCheckService
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _conflictLogBackoff = new();

        private readonly HealthCheckService _webHostCheck;
        private readonly IScriptHostManager _manager;
        private readonly ILogger _logger;

        public DynamicHealthCheckService(
            HealthCheckService webHostCheck,
            IScriptHostManager manager,
            ILogger<DynamicHealthCheckService> logger)
        {
            _webHostCheck = webHostCheck ?? throw new ArgumentNullException(nameof(webHostCheck));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default)
        {
            Task<HealthReport> webHostReport = _webHostCheck.CheckHealthAsync(predicate, cancellationToken);
            Task<HealthReport?> scriptHostReport = CheckScriptHostHealthAsync(predicate, cancellationToken);
            return MergeReports(await webHostReport, await scriptHostReport);
        }

        private HealthReport MergeReports(HealthReport left, HealthReport? right)
        {
            if (right == null)
            {
                return left;
            }

            // Merge the entries of both reports. If there are any duplicate keys, keep the left one.
            Dictionary<string, HealthReportEntry> entries = new(left.Entries);
            foreach ((string key, HealthReportEntry value) in right.Entries)
            {
                if (!entries.TryAdd(key, value))
                {
                    bool log = true;
                    if (_conflictLogBackoff.TryGetValue(key, out DateTimeOffset lastLogTime))
                    {
                        // ensure we log this only once per hour
                        log = DateTimeOffset.UtcNow - lastLogTime > TimeSpan.FromHours(1);
                    }

                    if (log)
                    {
                        _conflictLogBackoff[key] = DateTimeOffset.UtcNow;
                        Log.DuplicateHealthCheckEntry(_logger, key);
                    }
                }
            }

            // take the worst status.
            HealthStatus status = left.Status < right.Status
                ? left.Status : right.Status;

            // take the longest duration.
            TimeSpan duration = left.TotalDuration > right.TotalDuration
                ? left.TotalDuration : right.TotalDuration;

            return new(entries, status, duration);
        }

        private async Task<HealthReport?> CheckScriptHostHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default)
        {
            HealthCheckService? scriptHostCheck = _manager.Services.GetService<HealthCheckService>();
            if (scriptHostCheck is null)
            {
                Log.ScriptHostNoHealthCheckService(_logger);
                return null;
            }

            return await scriptHostCheck.CheckHealthAsync(predicate, cancellationToken);
        }

        private static partial class Log
        {
            [LoggerMessage(0, LogLevel.Debug, "Script host does not have a health check service. Skipping script host health checks.")]
            public static partial void ScriptHostNoHealthCheckService(ILogger logger);

            [LoggerMessage(1, LogLevel.Warning, "Duplicate health check entry '{HealthCheck}' found when merging health check reports. Keeping the first entry.")]
            public static partial void DuplicateHealthCheckEntry(ILogger logger, string healthCheck);
        }
    }
}
