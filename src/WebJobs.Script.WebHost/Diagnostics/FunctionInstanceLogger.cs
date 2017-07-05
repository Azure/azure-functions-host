// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    // Adapter for capturing SDK events and logging them to tables.
    internal class FunctionInstanceLogger : IAsyncCollector<FunctionInstanceLogEntry>
    {
        private const string Key = "metadata";

        private readonly ILogWriter _writer;

        private readonly Func<string, FunctionDescriptor> _funcLookup;

        private readonly IMetricsLogger _metrics;

        public FunctionInstanceLogger(
            Func<string, FunctionDescriptor> funcLookup,
            IMetricsLogger metrics,
            string hostName,
            string accountConnectionString,
            TraceWriter trace) : this(funcLookup, metrics)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
            var client = account.CreateCloudTableClient();
            var tableProvider = LogFactory.NewLogTableProvider(client);

            string containerName = Environment.MachineName;
            this._writer = LogFactory.NewWriter(hostName, containerName, tableProvider, (e) => OnException(e, trace));
        }

        internal FunctionInstanceLogger(
            Func<string, FunctionDescriptor> funcLookup,
            IMetricsLogger metrics)
        {
            _metrics = metrics;
            _funcLookup = funcLookup;
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

                    FunctionDescriptor descr = _funcLookup(shortName);
                    FunctionLogger logInfo = descr.Invoker.LogInfo;
                    state = new FunctionInstanceMonitor(descr.Metadata, _metrics, item.FunctionInstanceId, logInfo);

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

        public Task FlushAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_writer == null)
            {
                return Task.CompletedTask;
            }
            return _writer.FlushAsync();
        }

        public static void OnException(Exception exception, TraceWriter trace)
        {
            string errorString = $"Error writing logs to table storage: {exception.ToString()}";
            trace.Error(errorString, exception);
        }
    }
}
