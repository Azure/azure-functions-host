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
        const string RowKeyFormat = "{0}-{1:D20}-{2}"; // hostname-functionName-timeBucket-hostId

        public static TimelineAggregateEntity New(string containerName, FunctionId functionId, DateTime time, string salt)
        {
            return new TimelineAggregateEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = RowKeyTimeInterval(functionId, time, salt),
                Timestamp = DateTime.UtcNow,
                ContainerName = containerName
            };
        }

        public static TableQuery<TimelineAggregateEntity> GetQuery(FunctionId functionId, DateTime start, DateTime end)
        {
            string rowKeyStart = RowKeyTimeIntervalPrefix(functionId, start);
            string rowKeyEnd = RowKeyTimeIntervalPrefix(functionId, end);

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
        internal static string RowKeyTimeIntervalPrefix(FunctionId functionId, DateTime dateTime)
        {
            var bucket = TimeBucket.ConvertToBucket(dateTime);
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyPrefixFormat, functionId, bucket);
            return rowKey;
        }


        internal static string RowKeyTimeInterval(FunctionId functionId, DateTime dateTime, string hostId)
        {
            var bucket = TimeBucket.ConvertToBucket(dateTime);
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyFormat, functionId, bucket, hostId);
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
            string time = TableScheme.GetNthTerm(this.RowKey, 3);
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