// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Globalization;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Index that provides list of recention invocations per function type.
    // 1 entity per Intance of a function that's executed. 
    internal class RecentPerFuncEntity : TableEntity, IRecentFunctionEntry, IEntityWithEpoch
    {
        const string PartitionKeyFormat = TableScheme.RecentFuncIndexPK;
        const string RowKeyPrefix = "{0}-{1:D20}-";
        const string RowKeyFormat = "{0}-{1:D20}-{2}"; // functionId, timeBucket(descending), salt
        
        internal static RecentPerFuncEntity New(string containerName, FunctionInstanceLogItem item)
        {
            return new RecentPerFuncEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = RowKeyTimeStampDescending(item.FunctionId, item.StartTime, item.FunctionInstanceId),
                FunctionName = item.FunctionName,
                DisplayName = item.GetDisplayTitle(),
                FunctionInstanceId = item.FunctionInstanceId.ToString(),
                StartTime = item.StartTime,
                FunctionInstanceHeartbeatExpiry = item.FunctionInstanceHeartbeatExpiry,
                EndTime = item.EndTime,
                HasError = item.HasError,
                ContainerName = containerName
            };
        }

        internal static TableQuery<RecentPerFuncEntity> GetRecentFunctionsQuery(
            RecentFunctionQuery queryParams
            )
        {
            var functionId = queryParams.FunctionId;
            var start = queryParams.Start;
            var end = queryParams.End;

            string rowKeyStart = RowKeyTimeStampDescendingPrefix(functionId, end);

            // add a tick to create a greater row key so that we lexically compare
            var start2 = (start == DateTime.MinValue) ? start : start.AddTicks(-1);
            string rowKeyEnd = RowKeyTimeStampDescendingPrefix(functionId, start2);

            string partKey = PartitionKeyFormat;

            var rangeQuery = TableScheme.GetRowsInRange<RecentPerFuncEntity>(
                partKey, rowKeyStart, rowKeyEnd);

            if (queryParams.MaximumResults > 0)
            {
                rangeQuery = rangeQuery.Take(queryParams.MaximumResults);
            }
            return rangeQuery;
        }

        private bool _hasError;

        public DateTime GetEpoch()
        {
            return this.StartTime;
        }

        // No salt. This is a prefix, so we'll pick up all ranges.
        private static string RowKeyTimeStampDescendingPrefix(FunctionId functionId, DateTime startTime)
        {
            var x = (DateTime.MaxValue.Ticks - startTime.Ticks);

            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyPrefix,
                functionId, x);
            return rowKey;
        }

        // Salt must be deterministic. 
        internal static string RowKeyTimeStampDescending(FunctionId functionId, DateTime startTime, Guid salt)
        {
            var x = (DateTime.MaxValue.Ticks - startTime.Ticks);

            // Need Salt since timestamp may not be unique
            int salt2 = salt.GetHashCode();
            string rowKey = string.Format(CultureInfo.InvariantCulture, RowKeyFormat, 
                functionId, x, salt2);
            return rowKey;
        }

        public string ContainerName { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        // Last heart beat. When EndTime is missing, this lets us know the function is still alive. 
        public DateTime? FunctionInstanceHeartbeatExpiry { get; set; }

        Guid IFunctionInstanceBaseEntry.FunctionInstanceId
        {
            get
            {
                return Guid.Parse(this.FunctionInstanceId);
            }
        }

        // Raw guid. 
        public string FunctionInstanceId
        {
            get; set;
        }

        public string FunctionName { get; set; }

        public string DisplayName { get; set; }
        
        public bool HasError
        {
            get
            {
                // Use RawStatus for legacy check. 
                return _hasError || (this.RawStatus == "CompletedFailed");
            }
            set
            {
                _hasError = value;
            }
        }

        /// <summary>
        ///  Legacy for errors.
        ///  Original ("legacy") log writers would write out the function status. 
        ///  Now, we write separate bits (start time, end time, HasError) and infer the status from that. 
        ///  However, for the legacy case, it didn't have HasError and so we need to detect an error if the status field was set to "CompletedFailed".
        ///  New cases don't even write this status field anymore.  
        /// </summary>
        public string RawStatus { get; set; }
    }
}