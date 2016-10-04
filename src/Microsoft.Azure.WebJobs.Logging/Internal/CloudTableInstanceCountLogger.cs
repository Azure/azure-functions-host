// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging.Internal
{
    /// <summary>
    /// Log function instance counts to a Azure Table
    /// </summary>
    public class CloudTableInstanceCountLogger : InstanceCountLoggerBase
    {
        private readonly ILogTableProvider _tableLookup;
        private readonly string _containerName;

        private readonly int _containerSize;

        /// <summary>
        /// Rate at which to poll and call WriteEntry(). 
        /// </summary>
        public TimeSpan PollingInterval
        {
            get; set;
        }

        ///
        public CloudTableInstanceCountLogger(string containerName, ILogTableProvider tableLookup, int containerSize)
        {
            // Default polling interval
            this.PollingInterval = TimeSpan.FromSeconds(60);

            this._tableLookup = tableLookup;
            this._containerName = containerName;
            this._containerSize = containerSize;
        }

        /// 
        protected override async Task WriteEntry(long ticks, int currentActive, int totalThisPeriod)
        {
            if (currentActive == 0 && totalThisPeriod == 0)
            {
                return; // skip logging if no activity 
            }
            var entity = new InstanceCountEntity(ticks, _containerName)
            {
                CurrentActive = currentActive,
                TotalThisPeriod = totalThisPeriod,
                MachineSize = _containerSize,
                DurationMilliseconds = (int) this.PollingInterval.TotalMilliseconds
            };

            TableOperation opInsert = TableOperation.Insert(entity);

            var instanceTable = _tableLookup.GetTableForDateTime(new DateTime(ticks, DateTimeKind.Utc));
            await instanceTable.SafeExecuteAsync(opInsert);
        }

        /// <summary>
        /// Poll, return the ticks. 
        /// </summary>
        /// <param name="token">cancellation token to interupt the poll. Don't throw when cancelled, just return early.</param>
        /// <returns>Tick counter after the poll.</returns>
        protected override async Task<long> WaitOnPoll(CancellationToken token)
        {
            try
            {
                await Task.Delay(PollingInterval, token);
            }
            catch (OperationCanceledException)
            {
                // Don't return yet. One last chance to flush 
            }

            return DateTime.UtcNow.Ticks;
        }
    }
}