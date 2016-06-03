// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging.Internal
{
    /// <summary>
    /// Entity for logging function instance counts
    /// </summary>
    public class InstanceCountEntity : TableEntity
    {
        const string PartitionKeyFormat = TableScheme.InstanceCountPK;
        const string RowKeyPrefix = "{0:D20}-";
        const string RowKeyFormat = "{0:D20}-{1}-{2}"; // timestamp ticks, container name, salt

        // Have a salt value for writing to avoid collisions since timeBucket is not gauranteed to be unique
        // when many functions are quickly run within a single time tick. 
        static int _salt;

        ///
        public InstanceCountEntity()
        {
        }

        // from rowKey
        ///
        public long GetTicks()
        {
            var time = TableScheme.Get1stTerm(this.RowKey);
            long ticks = long.Parse(time, CultureInfo.InvariantCulture);
            return ticks;
        }

        ///
        public long GetEndTicks()
        {
            var startTicks = GetTicks();
            var endTime = new DateTime(startTicks).AddMilliseconds(this.DurationMilliseconds);
            var endTicks = endTime.Ticks;
            return endTicks;
        }

        /// 
        public long GetDurationInTicks()
        {
            return TimeSpan.FromMilliseconds(DurationMilliseconds).Ticks;
        }

        ///
        public InstanceCountEntity(long ticks, string containerName)
        {
            int salt = Interlocked.Increment(ref _salt);

            this.PartitionKey = PartitionKeyFormat;
            this.RowKey = string.Format(CultureInfo.InvariantCulture, RowKeyFormat, ticks, containerName, salt);
        }

        ///
        public static TableQuery<InstanceCountEntity> GetQuery(DateTime startTime, DateTime endTime)
        {
            if (startTime > endTime)
            {
                throw new InvalidOperationException("Start time must be less than or equal to end time");
            }
            string rowKeyStart = string.Format(CultureInfo.InvariantCulture, RowKeyPrefix, startTime.Ticks);
            string rowKeyEnd = string.Format(CultureInfo.InvariantCulture, RowKeyPrefix, endTime.Ticks);

            var query = TableScheme.GetRowsInRange<InstanceCountEntity>(PartitionKeyFormat, rowKeyStart, rowKeyEnd);
            return query;
        }

        /// <summary>
        /// Number of individual instances active in this period
        /// </summary>
        public int CurrentActive { get; set; }

        /// <summary>
        /// Number of total instances started during this period
        /// </summary>
        public int TotalThisPeriod { get; set; }

        /// <summary>
        /// Size of the machine that these instances ran on.  
        /// </summary>
        public int MachineSize { get; set; }

        /// <summary>
        /// Polling duration in MS. 
        /// </summary>
        public int DurationMilliseconds { get; set; }
    }
}