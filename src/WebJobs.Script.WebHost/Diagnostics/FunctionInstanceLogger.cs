// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class FunctionInstanceLogger : IAsyncCollector<FunctionInstanceLogEntry>
    {
        private const string Key = "metadata";

        private readonly IMetricsLogger _metrics;
        private readonly IFunctionMetadataManager _metadataManager;
        private ConcurrentDictionary<(string, string, bool, bool), string> _eventDataCache = new ConcurrentDictionary<(string, string, bool, bool), string>();
        private ConcurrentDictionary<BindingMetadata, string> _bindingMetricEventNames = new ConcurrentDictionary<BindingMetadata, string>();

        public FunctionInstanceLogger(
            IFunctionMetadataManager metadataManager,
            IMetricsLogger metrics,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
            : this(metadataManager, metrics)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            string accountConnectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Dashboard);
            if (accountConnectionString != null)
            {
                loggerFactory.CreateLogger<FunctionInstanceLogger>().LogWarning($"The {ConnectionStringNames.Dashboard} setting is no longer supported. See https://aka.ms/functions-dashboard for details.");
            }
        }

        internal FunctionInstanceLogger(IFunctionMetadataManager metadataManager, IMetricsLogger metrics)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
        }

        public Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (item.IsStart)
            {
                StartFunction(item);
            }
            else if (item.IsCompleted)
            {
                EndFunction(item);
            }

            return Task.CompletedTask;
        }

        private void StartFunction(FunctionInstanceLogEntry item)
        {
            if (_metadataManager.TryGetFunctionMetadata(item.LogName, out FunctionMetadata function))
            {
                var startedEvent = new FunctionStartedEvent(item.FunctionInstanceId, function);
                _metrics.BeginEvent(startedEvent);

                var invokeLatencyEvent = LogInvocationMetrics(function);
                item.Properties[Key] = (startedEvent, invokeLatencyEvent);
            }
            else
            {
                throw new InvalidOperationException($"Unable to load metadata for function '{item.LogName}'.");
            }
        }

        internal object LogInvocationMetrics(FunctionMetadata metadata)
        {
            // log events for each of the binding types used
            foreach (var binding in metadata.Bindings)
            {
                string eventName = _bindingMetricEventNames.GetOrAdd(binding, static (b) =>
                {
                    return string.Format(MetricEventNames.FunctionBindingTypeFormat, b.Type);
                });
                _metrics.LogEvent(eventName, metadata.Name);
            }

            return _metrics.BeginEvent(MetricEventNames.FunctionInvokeLatency, metadata.Name);
        }

        private void EndFunction(FunctionInstanceLogEntry item)
        {
            item.Properties.TryGetValue(Key, out (FunctionStartedEvent, object) invocationTuple);

            bool success = item.ErrorDetails == null;
            var startedEvent = invocationTuple.Item1;
            startedEvent.Success = success;

            var function = startedEvent.FunctionMetadata;
            string eventName = success ? MetricEventNames.FunctionInvokeSucceeded : MetricEventNames.FunctionInvokeFailed;
            string functionName = function != null ? function.Name : string.Empty;

            // This has low cardinality but we allocate the string every call (even though ironically it's often not used due to rollups)
            // It's cheaper to cache and lookup rather than generate and pay for GC here.
            // Note: this would be faster with a readonly record struct key, but StyleCop is angry with it until an upcoming 1.2.x beta release
            var key = (startedEvent.FunctionMetadata.Language, functionName, success, Stopwatch.IsHighResolution);
            if (!_eventDataCache.TryGetValue(key, out var data))
            {
                data = string.Format(Microsoft.Azure.WebJobs.Script.Properties.Resources.FunctionInvocationMetricsData, startedEvent.FunctionMetadata.Language, functionName, success, Stopwatch.IsHighResolution);
                _eventDataCache[key] = data;
            }
            _metrics.LogEvent(eventName, startedEvent.FunctionName, Sanitizer.Sanitize(data));

            startedEvent.Data = data;
            _metrics.EndEvent(startedEvent);

            var invokeLatencyEvent = invocationTuple.Item2;
            if (invokeLatencyEvent is MetricEvent metricEvent)
            {
                metricEvent.Data = data;
            }

            _metrics.EndEvent(invokeLatencyEvent);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken)) => Task.CompletedTask;
    }
}