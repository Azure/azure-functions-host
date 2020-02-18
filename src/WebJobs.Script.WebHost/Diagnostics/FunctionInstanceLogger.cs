// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
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

        public FunctionInstanceLogger(
            IFunctionMetadataManager metadataManager,
            IMetricsLogger metrics,
            IHostIdProvider hostIdProvider,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
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

        internal FunctionInstanceLogger(IFunctionMetadataManager metadataManager, IMetricsLogger metrics)
        {
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
        }

        public async Task AddAsync(FunctionInstanceLogEntry item, CancellationToken cancellationToken = default(CancellationToken))
        {
            FunctionInstanceMonitor state;
            item.Properties.TryGetValue(Key, out state);

            if (item.EndTime.HasValue)
            {
                // Completed
                bool success = item.ErrorDetails == null;
                state.End(success);
            }
            else
            {
                // Started
                if (state == null)
                {
                    string shortName = Utility.GetFunctionShortName(item.FunctionName);
                    FunctionMetadata function = GetFunctionMetadata(shortName);

                    if (function == null)
                    {
                        // This exception will cause the function to not get executed.
                        throw new InvalidOperationException($"Missing function.json for '{shortName}'.");
                    }
                    state = new FunctionInstanceMonitor(function, _metrics, item.FunctionInstanceId);

                    item.Properties[Key] = state;

                    state.Start();
                }
            }

            if (_writer != null)
            {
                await _writer.AddAsync(new FunctionInstanceLogItem
                {
                    FunctionInstanceId = item.FunctionInstanceId,
                    FunctionName = Utility.GetFunctionShortName(item.FunctionName),
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

        private FunctionMetadata GetFunctionMetadata(string functionName)
        {
            FunctionMetadata GetMetadataFromCollection(IEnumerable<FunctionMetadata> functions)
            {
                return functions.FirstOrDefault(p => Utility.FunctionNamesMatch(p.Name, functionName));
            }

            return GetMetadataFromCollection(_metadataManager.GetFunctionMetadata());
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
            string errorString = $"Error writing logs to table storage: {exception.ToString()}";
            logger.LogError(errorString, exception);
        }
    }
}
