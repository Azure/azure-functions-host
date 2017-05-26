// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Entity to track active compute Containers during a timerange.
    // A Container is a billable compute unit, like a virtual machine. 
    // A container may run many functions simultaneously. 
    internal class ContainerActiveEntity : TableEntity
    {
        const string PartitionKeyFormat = TableScheme.ContainerActivePK;
        const string RowKeyPrefixTimeFormat = "{0:D20}-";
        const string RowKeyFormat = "{0:D20}-{1}-"; // timebucket, containerName

        public static ContainerActiveEntity New(DateTime now, string containerName)
        {
            return new ContainerActiveEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = RowKeyTimeInterval(now, containerName),
                StartTime = now
            };
        }


        internal static string RowKeyTimeInterval(DateTime dateTime, string containerName)
        {
            var bucket = TimeBucket.ConvertToBucket(dateTime);
            return RowKeyTimeInterval(bucket, containerName);
        }

        // Time first, support range queries in a time window. 
        internal static string RowKeyTimeInterval(long timeBucket, string containerName)
        {
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyFormat, timeBucket, TableScheme.NormalizeContainerName(containerName));
            return rowKey;
        }


        public static TableQuery<ContainerActiveEntity> GetQuery(DateTime start, DateTime end)
        {
            string rowKeyStart = RowKeyTimeIntervalPrefix(start);
            string rowKeyEnd = RowKeyTimeIntervalPrefix(end);

            var rangeQuery = TableScheme.GetRowsInRange<ContainerActiveEntity>(
                TableScheme.ContainerActivePK,
                  rowKeyStart, rowKeyEnd);

            return rangeQuery;
        }

        internal static string RowKeyTimeIntervalPrefix(DateTime dateTime)
        {
            var bucket = TimeBucket.ConvertToBucket(dateTime);
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyPrefixTimeFormat, bucket);
            return rowKey;
        }

        // Used to fetch an existing container entry so we can append it. 
        // Returns null if not found. 
        public static async Task<ContainerActiveEntity> LookupAsync(CloudTable table, long timeBucket, string containerName)
        {
            TableOperation retrieveOperation = TableOperation.Retrieve<ContainerActiveEntity>(
                TableScheme.ContainerActivePK,
                RowKeyTimeInterval(timeBucket, containerName));

            // Execute the retrieve operation.
            ContainerActiveEntity result;
            try
            {
                TableResult retrievedResult = await table.ExecuteAsync(retrieveOperation);
                result = (ContainerActiveEntity)retrievedResult.Result;                
            }
            catch (StorageException e)
            {
                var code = (HttpStatusCode)e.RequestInformation.HttpStatusCode;
                if (code != HttpStatusCode.NotFound)
                {
                    throw;
                }
                result = null;
            }
            return result;
        }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public long GetStartBucket()
        {
            return TimeBucket.ConvertToBucket(this.StartTime);
        }

        public long GetLength()
        {
            var len = (long)(EndTime - StartTime).TotalMinutes;
            if (len <= 0)
            {
                len = 1;
            }
            return len;
        }

        public string GetContainerName()
        {
            // extract from rowKey 
            // {time}-{container}-
            string containerName = TableScheme.Get2ndTerm(this.RowKey);
            return containerName;
        }
    }
}