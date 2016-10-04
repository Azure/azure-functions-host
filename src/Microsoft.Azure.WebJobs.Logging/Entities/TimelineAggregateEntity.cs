// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Entity per minute per function type, aggregated. 
    //  This lets clients do a timeline query to see activity in a given window.  
    internal class TimelineAggregateEntity : TableEntity, IAggregateEntry, IEntityWithEpoch
    {
        // HostId in the rowKey is additional salt in case multiple hosts are writing in the same timeline. It is ignored during read.
        const string PartitionKeyFormat = TableScheme.TimelineAggregatePK;
        const string RowKeyPrefixFormat = "{0}-{1:D20}-"; 
        const string RowKeyFormat       = "{0}-{1:D20}-{2}"; // functionName\timeBucket\hostId

        public static TimelineAggregateEntity New(string containerName, string functionId, DateTime time, string hostId)
        {
            return new TimelineAggregateEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = RowKeyTimeInterval(functionId, time, hostId),
                Timestamp = DateTime.UtcNow,
                ContainerName = containerName
            };
        }

        public static TableQuery<TimelineAggregateEntity> GetQuery(string functionName, DateTime start, DateTime end)
        {
            string rowKeyStart = RowKeyTimeIntervalPrefix(functionName, start);
            string rowKeyEnd = RowKeyTimeIntervalPrefix(functionName, end);

            var rangeQuery = TableScheme.GetRowsInRange<TimelineAggregateEntity>(
                PartitionKeyFormat,
                  rowKeyStart, rowKeyEnd);

            return rangeQuery;
        }

        public DateTime GetEpoch()
        {
            var bucket = GetTimeBucket();
            return TimeBucket.ConvertToDateTime(bucket);         
        }

        // Reader, no salt
        internal static string RowKeyTimeIntervalPrefix(string functionId, DateTime dateTime)
        {
            var bucket = TimeBucket.ConvertToBucket(dateTime);
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyPrefixFormat, TableScheme.NormalizeFunctionName(functionId), bucket);
            return rowKey;
        }


        internal static string RowKeyTimeInterval(string functionId, DateTime dateTime, string hostId)
        {
            var bucket = TimeBucket.ConvertToBucket(dateTime);
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyFormat, TableScheme.NormalizeFunctionName(functionId), bucket, hostId);
            return rowKey;
        }

        // Extract the time bucket from the RowKey 
        long IAggregateEntry.TimeBucket
        {
            get
            {
                return GetTimeBucket();
            }
        }
        private long GetTimeBucket()
        {
            string time = TableScheme.Get2ndTerm(this.RowKey);
            var minute = long.Parse(time, CultureInfo.InvariantCulture);
            return minute;
        }

        public string ContainerName { get; set; }

        DateTime IAggregateEntry.Time
        {
            get
            {                
                var bucket = GetTimeBucket();
                return TimeBucket.ConvertToDateTime(bucket);
            }
        }
         
        public int TotalRun { get; set; }

        public int TotalPass { get; set; }
        public int TotalFail { get; set; }
    }
}