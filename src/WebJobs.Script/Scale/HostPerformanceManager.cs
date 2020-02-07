// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    public class HostPerformanceManager : IDisposable
    {
        private const int MinSampleCount = 5;
        private readonly IEnvironment _environment;
        private readonly IOptions<HostHealthMonitorOptions> _healthMonitorOptions;
        private readonly ProcessMonitor _processMonitor;
        private readonly IServiceProvider _serviceProvider;
        private bool _disposed = false;

        public HostPerformanceManager(IEnvironment environment, IOptions<HostHealthMonitorOptions> healthMonitorOptions, IServiceProvider serviceProvider, ProcessMonitor processMonitor = null)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }
            if (healthMonitorOptions == null)
            {
                throw new ArgumentNullException(nameof(healthMonitorOptions));
            }

            _environment = environment;
            _healthMonitorOptions = healthMonitorOptions;
            _serviceProvider = serviceProvider;
            _processMonitor = processMonitor ?? new ProcessMonitor(Process.GetCurrentProcess(), environment);

            _processMonitor.Start();
        }

        /// <summary>
        /// Check sandbox enforced performance counters
        /// </summary>
        public virtual bool PerformanceCountersExceeded(Collection<string> exceededCounters = null, ILogger logger = null)
        {
            var counters = GetPerformanceCounters(logger);
            if (counters != null)
            {
                return PerformanceCounterThresholdsExceeded(counters, exceededCounters, _healthMonitorOptions.Value.CounterThreshold);
            }

            return false;
        }

        /// <summary>
        /// Check both sandbox enforced performance counters as well as process level thresholds
        /// like CPU, memory, etc.
        /// </summary>
        public virtual async Task<bool> IsUnderHighLoadAsync(Collection<string> exceededCounters = null, ILogger logger = null)
        {
            return PerformanceCountersExceeded(exceededCounters, logger) ||
                await ProcessThresholdsExceeded(exceededCounters, logger);
        }

        public async Task<IActionResult> TryHandleHealthPingAsync(HttpRequest request, ILogger logger)
        {
            var healthPingEnabled = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.HealthPingEnabled, "1");
            if (healthPingEnabled.Equals("0"))
            {
                // disabled at app level
                return null;
            }

            bool checkHealth = false;
            var userAgent = request.GetHeaderValueOrDefault("User-Agent");
            if (!string.IsNullOrEmpty(userAgent) &&
                (userAgent.IndexOf(ScriptConstants.HttpScaleUserAgent, StringComparison.OrdinalIgnoreCase) != -1 ||
                userAgent.IndexOf(ScriptConstants.ScaleControllerUserAgent, StringComparison.OrdinalIgnoreCase) != -1))
            {
                // for these user agents, we default to true
                checkHealth = true;
            }

            var query = request.GetQueryCollectionAsDictionary();
            if (query.TryGetValue(ScriptConstants.HealthCheckQueryParam, out string value))
            {
                // header overrides user agent
                checkHealth = value.Equals("1");
            }

            if (checkHealth)
            {
                // check host + worker health
                int statusCode = (int)HttpStatusCode.OK;
                if (await IsUnderHighLoadAsync(logger: logger))
                {
                    statusCode = 429;
                }

                return new StatusCodeResult(statusCode);
            }

            return null;
        }

        internal async Task<bool> ProcessThresholdsExceeded(Collection<string> exceededCounters = null, ILogger logger = null)
        {
            var hostProcessStats = _processMonitor.GetStats();
            if (hostProcessStats.CpuLoadHistory.Any())
            {
                string formattedLoadHistory = string.Join(",", hostProcessStats.CpuLoadHistory);
                logger?.HostProcessCpuStats(_environment.GetEffectiveCoresCount(), formattedLoadHistory, Math.Round(hostProcessStats.CpuLoadHistory.Average()), Math.Round(hostProcessStats.CpuLoadHistory.Max()));
            }

            double workersAverageCpuTotal = 0;
            var dispatcher = GetDispatcherAsync();
            if (dispatcher != null)
            {
                // If OOP, check worker stats
                var workerStatuses = await dispatcher.GetWorkerStatusesAsync();
                if (workerStatuses.All(p => p.Value.ProcessStats.CpuLoadHistory.Count() > MinSampleCount))
                {
                    // first compute the average CPU load for each worker
                    var averageWorkerCpuStats = new List<double>();
                    foreach (var currentStatus in workerStatuses)
                    {
                        // take the last N samples
                        var workerProcessStats = currentStatus.Value.ProcessStats;
                        int currWorkerProcessCpuStatsCount = workerProcessStats.CpuLoadHistory.Count();
                        var currWorkerCpuStats = workerProcessStats.CpuLoadHistory.Skip(currWorkerProcessCpuStatsCount - MinSampleCount).Take(MinSampleCount).Average();
                        averageWorkerCpuStats.Add(currWorkerCpuStats);
                    }

                    // compute the final average CPU total across all workers
                    workersAverageCpuTotal = averageWorkerCpuStats.Sum();
                }
            }

            // calculate the aggregate load of host + workers (if OOP)
            int hostProcessCpuStatsCount = hostProcessStats.CpuLoadHistory.Count();
            if (hostProcessCpuStatsCount > MinSampleCount)
            {
                // compute the aggregate average CPU usage for host + workers for the last MinSampleCount samples
                var hostAverageCpu = hostProcessStats.CpuLoadHistory.Skip(hostProcessCpuStatsCount - MinSampleCount).Take(MinSampleCount).Average();
                var aggregateAverage = Math.Round(hostAverageCpu + workersAverageCpuTotal);
                logger?.HostAggregateCpuLoad(aggregateAverage);

                // if the average is above our threshold, return true (we're overloaded)
                var adjustedThreshold = _healthMonitorOptions.Value.CounterThreshold * 100;
                if (aggregateAverage >= adjustedThreshold)
                {
                    logger?.HostCpuThresholdExceeded(aggregateAverage, adjustedThreshold);
                    if (exceededCounters != null)
                    {
                        exceededCounters.Add("CPU");
                    }
                    return true;
                }
            }

            return false;
        }

        internal static bool PerformanceCounterThresholdsExceeded(ApplicationPerformanceCounters counters, Collection<string> exceededCounters = null, float threshold = HostHealthMonitorOptions.DefaultCounterThreshold)
        {
            bool exceeded = false;

            // determine all counters whose limits have been exceeded
            exceeded |= ThresholdExceeded("ActiveConnections", counters.ActiveConnections, counters.ActiveConnectionLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Connections", counters.Connections, counters.ConnectionLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Threads", counters.Threads, counters.ThreadLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Processes", counters.Processes, counters.ProcessLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("NamedPipes", counters.NamedPipes, counters.NamedPipeLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Sections", counters.Sections, counters.SectionLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("RemoteDirMonitors", counters.RemoteDirMonitors, counters.RemoteDirMonitorLimit, threshold, exceededCounters);

            return exceeded;
        }

        internal static bool ThresholdExceeded(string name, long currentValue, long limit, float threshold, Collection<string> exceededCounters = null)
        {
            if (limit <= 0)
            {
                // no limit to apply
                return false;
            }

            float currentUsage = (float)currentValue / limit;
            bool exceeded = currentUsage > threshold;
            if (exceeded && exceededCounters != null)
            {
                exceededCounters.Add(name);
            }
            return exceeded;
        }

        internal ApplicationPerformanceCounters GetPerformanceCounters(ILogger logger = null)
        {
            string json = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteAppCountersName);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    // TEMP: need to parse this specially to work around bug where
                    // sometimes an extra garbage character occurs after the terminal
                    // brace
                    int idx = json.LastIndexOf('}');
                    if (idx > 0)
                    {
                        json = json.Substring(0, idx + 1);
                    }

                    return JsonConvert.DeserializeObject<ApplicationPerformanceCounters>(json);
                }
                catch (JsonReaderException ex)
                {
                    logger.LogError($"Failed to deserialize application performance counters. JSON Content: \"{json}\"", ex);
                }
            }

            return null;
        }

        private IFunctionInvocationDispatcher GetDispatcherAsync()
        {
            var hostManager = _serviceProvider.GetService<IScriptHostManager>();
            var dispatcherFactory = (hostManager as IServiceProvider)?.GetService<IFunctionInvocationDispatcherFactory>();
            if (dispatcherFactory != null)
            {
                var dispatcher = dispatcherFactory.GetFunctionDispatcher();
                if (dispatcher.State == FunctionInvocationDispatcherState.Initialized)
                {
                    return dispatcher;
                }
            }
            return null;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _processMonitor?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
