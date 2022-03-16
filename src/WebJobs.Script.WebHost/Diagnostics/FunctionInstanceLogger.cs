// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    // Adapter for capturing SDK events and logging them to tables.
    internal class FunctionInstanceLogger : IAsyncCollector<FunctionInstanceLogEntry>
    {
        private const string Key = "metadata";

        private readonly ILogWriter _writer;
        private readonly IMetricsLogger _metrics;
        private readonly IFunctionMetadataManager _metadataManager;
        private ConcurrentDictionary<BindingMetadata, string> _bindingMetricEventNames = new ConcurrentDictionary<BindingMetadata, string>();

        public FunctionInstanceLogger(
            IFunctionMetadataManager metadataManager,
            IMetricsLogger metrics,
            IHostIdProvider hostIdProvider,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            IDelegatingHandlerProvider delegatingHandlerProvider)
            : this(metadataManager, metrics)
        {
            if (hostIdProvider == null)
            {
                throw new ArgumentNullException(nameof(hostIdProvider));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            if (delegatingHandlerProvider == null)
            {
                throw new ArgumentNullException(nameof(delegatingHandlerProvider));
            }

            string accountConnectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Dashboard);
            if (accountConnectionString != null)
            {
                CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
                var restConfig = new RestExecutorConfiguration { DelegatingHandler = delegatingHandlerProvider.Create() };
                var tableClientConfig = new TableClientConfiguration { RestExecutorConfiguration = restConfig };

                var client = new CloudTableClient(account.TableStorageUri, account.Credentials, tableClientConfig);
                var tableProvider = LogFactory.NewLogTableProvider(client);

                ILogger logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
                logger.LogDebug("Azure WebJobs Dashboard is enabled");

                string hostId = hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult() ?? "default";
                string containerName = Environment.MachineName;
                _writer = LogFactory.NewWriter(hostId, containerName, tableProvider, (e) => OnException(e, logger));
            }
        }

        internal FunctionInstanceLogger(IFunctionMetadataManager metadataManager, IMetricsLogger metrics)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
        }

        public async Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (item.IsStart)
            {
                StartFunction(item);
            }
            else if (item.IsCompleted)
            {
                EndFunction(item);
            }

            if (_writer != null)
            {
                await _writer.AddAsync(new FunctionInstanceLogItem
                {
                    FunctionInstanceId = item.FunctionInstanceId,
                    FunctionName = item.LogName,
                    StartTime = item.StartTime,
                    EndTime = item.EndTime,
                    TriggerReason = item.TriggerReason,
                    Arguments = item.Arguments,
                    ErrorDetails = item.ErrorDetails,
                    LogOutput = item.LogOutput,
                    ParentId = item.ParentId
                });
            }
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
                string eventName = _bindingMetricEventNames.GetOrAdd(binding, (existing) =>
                {
                    return string.Format(MetricEventNames.FunctionBindingTypeFormat, binding.Type);
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
            string data = string.Format(Microsoft.Azure.WebJobs.Script.Properties.Resources.FunctionInvocationMetricsData, startedEvent.FunctionMetadata.Language, functionName, success, Stopwatch.IsHighResolution);
            _metrics.LogEvent(eventName, startedEvent.FunctionName, data);

            startedEvent.Data = data;
            _metrics.EndEvent(startedEvent);

            var invokeLatencyEvent = invocationTuple.Item2;
            if (invokeLatencyEvent is MetricEvent metricEvent)
            {
                metricEvent.Data = data;
            }

            _metrics.EndEvent(invokeLatencyEvent);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_writer == null)
            {
                return Task.CompletedTask;
            }

            return _writer.FlushAsync();
        }

        public static void OnException(Exception exception, ILogger logger)
        {
            logger.LogError($"Error writing logs to table storage: {exception.ToString()}", exception);
        }
    }
}