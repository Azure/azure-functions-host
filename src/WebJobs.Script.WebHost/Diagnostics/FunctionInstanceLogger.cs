// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    // Adapter for capturing SDK events and logging them to tables.
    internal class FunctionInstanceLogger : IAsyncCollector<FunctionInstanceLogEntry>
    {
        private const string Key = "metadata";

        private readonly ILogWriter _writer;
        private readonly IProxyMetadataManager _proxyMetadataManager;
        private readonly IMetricsLogger _metrics;
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly Dictionary<string, FunctionMetadata> _functionMetadataMap;

        public FunctionInstanceLogger(
            IFunctionMetadataManager metadataManager,
            IProxyMetadataManager proxyMetadataManager,
            IMetricsLogger metrics,
            IHostIdProvider hostIdProvider,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
            : this(metadataManager, proxyMetadataManager, metrics)
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

            _functionMetadataMap = new Dictionary<string, FunctionMetadata>(StringComparer.OrdinalIgnoreCase);
            _functionMetadataMap.AddRange(_metadataManager.Functions.ToDictionary(p => p.Name));
            if (!_proxyMetadataManager.ProxyMetadata.Functions.IsDefaultOrEmpty)
            {
                _functionMetadataMap.AddRange(_proxyMetadataManager.ProxyMetadata.Functions.ToDictionary(p => p.Name));
            }

            string accountConnectionString = configuration.GetWebJobsConnectionString(ConnectionStringNames.Dashboard);
            if (accountConnectionString != null)
            {
                CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
                var client = account.CreateCloudTableClient();
                var tableProvider = LogFactory.NewLogTableProvider(client);

                ILogger logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

                string hostId = hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult() ?? "default";
                string containerName = Environment.MachineName;
                _writer = LogFactory.NewWriter(hostId, containerName, tableProvider, (e) => OnException(e, logger));
            }
        }

        internal FunctionInstanceLogger(IFunctionMetadataManager metadataManager, IProxyMetadataManager proxyMetadataManager, IMetricsLogger metrics)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _proxyMetadataManager = proxyMetadataManager ?? throw new ArgumentNullException(nameof(proxyMetadataManager));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
        }

        public async Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
        {
            FunctionInstanceMonitor monitor;
            item.Properties.TryGetValue(Key, out monitor);

            if (item.EndTime.HasValue)
            {
                // Function Completed
                bool success = item.ErrorDetails == null;
                monitor.End(success);
            }
            else
            {
                // Function Started
                if (monitor == null)
                {
                    if (_functionMetadataMap.TryGetValue(item.LogName, out FunctionMetadata function))
                    {
                        monitor = new FunctionInstanceMonitor(function, _metrics, item.FunctionInstanceId);
                        item.Properties[Key] = monitor;
                        monitor.Start();
                    }
                    else
                    {
                        // This exception will cause the function to not get executed.
                        throw new InvalidOperationException($"Missing function.json for '{item.LogName}'.");
                    }
                }
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
