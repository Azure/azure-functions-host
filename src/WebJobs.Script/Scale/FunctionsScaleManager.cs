﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Config;
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
        private readonly ITargetScalerManager _targetScalerManager;
        private readonly IConcurrencyStatusRepository _concurrencyStatusRepository;
        private readonly IFunctionsHostingConfiguration _functionsHostingConfiguration;
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;

        // for mock testing only
        public FunctionsScaleManager()
        {
        }

        public FunctionsScaleManager(
            IScaleMonitorManager monitorManager,
            IScaleMetricsRepository metricsRepository,
            ITargetScalerManager targetScalerManager,
            IConcurrencyStatusRepository concurrencyStatusRepository,
            IFunctionsHostingConfiguration functionsHostingConfiguration,
            IEnvironment environment,
            ILoggerFactory loggerFactory)
        {
            _monitorManager = monitorManager;
            _metricsRepository = metricsRepository;
            _targetScalerManager = targetScalerManager;
            _concurrencyStatusRepository = concurrencyStatusRepository;
            _functionsHostingConfiguration = functionsHostingConfiguration;
            _environment = environment;
            _logger = loggerFactory.CreateLogger<FunctionsScaleManager>();
        }

        /// <summary>
        /// Get the current scale status (vote) by querying all active monitors for their
        /// scale status.
        /// </summary>
        /// <param name="context">The context to use for the scale decision.</param>
        /// <returns>The scale vote.</returns>
        public virtual async Task<ScaleStatusResult> GetScaleStatusAsync(ScaleStatusContext context)
        {
            var scaleMonitors = _monitorManager.GetMonitors();
            var targetScalers = _targetScalerManager.GetTargetScalers();

            Utility.GetScaleInstancesToProcess(_environment, _functionsHostingConfiguration, scaleMonitors, targetScalers,
                out List<IScaleMonitor> scaleMonitorsToProcess, out List<ITargetScaler> targetScalersToProcess);

            var targetScalerVotes = await GetTargetScalersResult(context, targetScalersToProcess);

            return new ScaleStatusResult
            {
                Vote = await GetScaleMonitorsResult(context, scaleMonitorsToProcess, targetScalerVotes.Select(x => x.Vote)),
                TargetWorkerCount = targetScalerVotes.Any() ? targetScalerVotes.Select(x => x.TargetWorkerCount).Max() : null
            };
        }

        private async Task<ScaleVote> GetScaleMonitorsResult(ScaleStatusContext context, IEnumerable<IScaleMonitor> scaleMonitorsToProcess, IEnumerable<ScaleVote> targetScaleVotes)
        {
            List<ScaleVote> votes = new List<ScaleVote>();
            if (scaleMonitorsToProcess.Any())
            {
                // get the collection of current metrics for each monitor
                var monitorMetrics = await _metricsRepository.ReadMetricsAsync(scaleMonitorsToProcess);

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
                        var result = monitor.GetScaleStatus(scaleStatusContext);

                        _logger.LogDebug($"Monitor '{monitor.Descriptor.Id}' voted '{result.Vote.ToString()}'");
                        votes.Add(result.Vote);
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

            ScaleVote vote = GetAggregateScaleVote(votes.Union(targetScaleVotes), context, _logger);
            return vote;
        }

        private async Task<IEnumerable<TargetScalerVote>> GetTargetScalersResult(ScaleStatusContext context, IEnumerable<ITargetScaler> targetScalersToProcess)
        {
            List<TargetScalerVote> targetScaleVotes = new List<TargetScalerVote>();

            if (targetScalersToProcess.Any())
            {
                _logger.LogDebug($"{targetScalersToProcess.Count()} target scalers to sample");
                HostConcurrencySnapshot snapshot = null;
                try
                {
                    snapshot = await _concurrencyStatusRepository.ReadAsync(CancellationToken.None);
                }
                catch (Exception exc) when (!exc.IsFatal())
                {
                    _logger.LogError(exc, $"Failed to read concurrency status repository");
                }

                foreach (var targetScaler in targetScalersToProcess)
                {
                    try
                    {
                        TargetScalerContext targetScaleStatusContext = new TargetScalerContext();
                        if (snapshot != null)
                        {
                            if (snapshot.FunctionSnapshots.TryGetValue(targetScaler.TargetScalerDescriptor.FunctionId, out var functionSnapshot))
                            {
                                targetScaleStatusContext.InstanceConcurrency = functionSnapshot.Concurrency;
                                _logger.LogDebug($"Snapshot dynamic concurrency for target scaler '{targetScaler.TargetScalerDescriptor.FunctionId}' is '{functionSnapshot.Concurrency}'");
                            }
                        }
                        TargetScalerResult result = await targetScaler.GetScaleResultAsync(targetScaleStatusContext);
                        _logger.LogDebug($"Target worker count for '{targetScaler.TargetScalerDescriptor.FunctionId}' is '{result.TargetWorkerCount}'");
                        ScaleVote vote = ScaleVote.None;
                        if (context.WorkerCount > result.TargetWorkerCount)
                        {
                            vote = ScaleVote.ScaleIn;
                        }
                        else if (context.WorkerCount < result.TargetWorkerCount)
                        {
                            vote = ScaleVote.ScaleOut;
                        }
                        targetScaleVotes.Add(new TargetScalerVote
                        {
                            TargetWorkerCount = result.TargetWorkerCount,
                            Vote = vote
                        });
                    }
                    catch (Exception exc) when (!exc.IsFatal())
                    {
                        // if a particular target scaler fails, log and continue
                        _logger.LogError(exc, $"Failed to query scale result for target scaler '{targetScaler.TargetScalerDescriptor.FunctionId}'.");
                    }
                }
            }
            //int? targetWorkerCount = targetScaleVotes.Any() ? targetScaleVotes.Max() : null;
            return targetScaleVotes;
        }

        internal static ScaleVote GetAggregateScaleVote(IEnumerable<ScaleVote> votes, ScaleStatusContext context, ILogger logger)
        {
            ScaleVote vote = ScaleVote.None;
            if (votes.Any())
            {
                // aggregate all the votes into a single vote
                if (votes.Any(p => p == ScaleVote.ScaleOut))
                {
                    // scale out if at least 1 monitor requires it
                    logger.LogDebug("Scaling out based on votes");
                    vote = ScaleVote.ScaleOut;
                }
                else if (context.WorkerCount > 0 && votes.All(p => p == ScaleVote.ScaleIn))
                {
                    // scale in only if all monitors vote scale in
                    logger.LogDebug("Scaling in based on votes");
                    vote = ScaleVote.ScaleIn;
                }
            }
            else if (context.WorkerCount > 0)
            {
                // if no functions exist or are enabled we'll scale in
                logger.LogDebug("No enabled functions or scale votes so scaling in");
                vote = ScaleVote.ScaleIn;
            }

            return vote;
        }
    }
}
