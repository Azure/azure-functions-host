// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Manages scale monitoring operations.
    /// </summary>
    public class FunctionsScaleManager
    {
        private readonly IScaleMonitorManager _monitorManager;
        private readonly IScaleMetricsRepository _metricsRepository;
        private readonly ILogger _logger;

        // for mock testing only
        public FunctionsScaleManager()
        {
        }

        public FunctionsScaleManager(IScaleMonitorManager monitorManager, IScaleMetricsRepository metricsRepository, ILoggerFactory loggerFactory)
        {
            _monitorManager = monitorManager;
            _metricsRepository = metricsRepository;
            _logger = loggerFactory.CreateLogger<FunctionsScaleManager>();
        }

        /// <summary>
        /// Get the current scale status (vote) by querying all active monitors for their
        /// scale status.
        /// </summary>
        /// <param name="context">The context to use for the scale decision.</param>
        /// <returns>The scale vote.</returns>
        public virtual async Task<int> GetScaleStatusAsync(ScaleStatusContext context)
        {
            var monitors = _monitorManager.GetMonitors();

            List<int> votes = new List<int>();
            if (monitors.Any())
            {
                // get the collection of current metrics for each monitor
                var monitorMetrics = await _metricsRepository.ReadMetricsAsync(monitors);

                _logger.LogDebug($"Computing scale status (WorkerCount={context.WorkerCount})");
                _logger.LogDebug($"{monitorMetrics.Count} scale monitors to sample");

                // for each monitor, ask it to return its scale status (vote) based on
                // the metrics and context info (e.g. worker count)
                foreach (var pair in monitorMetrics)
                {
                    var monitor = pair.Key;
                    var metrics = pair.Value;

                    try
                    {
                        // create a new context instance to avoid modifying the
                        // incoming context
                        var scaleStatusContext = new ScaleStatusContext
                        {
                            WorkerCount = context.WorkerCount,
                            Metrics = metrics
                        };
                        var scaleStatus = monitor.GetScaleStatus(scaleStatusContext);
                        int vote = 0;
                        if (scaleStatus.TargetWorkerCount.HasValue)
                        {
                            vote = scaleStatus.TargetWorkerCount.Value;
                        }
                        else
                        {
                            vote = Convert.ToInt32(scaleStatus.Vote);
                        }
                        _logger.LogDebug($"Monitor '{monitor.Descriptor.Id}' voted '{vote}'");
                        votes.Add(vote);
                    }
                    catch (Exception exc) when (!exc.IsFatal())
                    {
                        // if a particular monitor fails, log and continue
                        _logger.LogError(exc, $"Failed to query scale status for monitor '{monitor.Descriptor.Id}'.");
                    }
                }
            }
            else
            {
                // no monitors registered
                // this can happen if the host is offline
            }

            var agregatedVote = GetAggregateScaleVote(votes, context, _logger);

            return agregatedVote;
        }

        internal static int GetAggregateScaleVote(List<int> votes, ScaleStatusContext context, ILogger logger)
        {
            int vote = 0;

            if (votes.Any())
            {
                // aggregate all the votes into a single vote
                if (votes.Any(p => p > 0))
                {
                    // scale out if at least 1 monitor requires it
                    logger.LogDebug("Scaling out based on votes");
                    vote = votes.Max(); // scale out on max vote
                }
                else if (context.WorkerCount > 0 && votes.All(p => p < 0))
                {
                    // scale in only if all monitors vote scale in
                    logger.LogDebug("Scaling in based on votes");
                    vote = votes.Where(x => x != 0).Max(); // scale in on max vote
                }
            }
            else if (context.WorkerCount > 0)
            {
                // if no functions exist or are enabled we'll scale in
                logger.LogDebug("No enabled functions or scale votes so scaling in");
                vote = -1;
            }

            return vote;
        }
    }
}
