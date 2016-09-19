// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.Azure.WebJobs.Logging.Internal;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Create a reader. 
    internal class LogReader : ILogReader
    {
        // All writing goes to 1 table. 
        private readonly CloudTable _instanceTable;

        public LogReader(CloudTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }
            this._instanceTable = table;
        }

        public async Task<FunctionVolumeTimelineEntry[]> GetVolumeAsync(DateTime startTime, DateTime endTime, int numberBuckets)        
        {
            var query = InstanceCountEntity.GetQuery(startTime, endTime);

            InstanceCountEntity[] rows = await _instanceTable.SafeExecuteQueryAsync(query);

            var startTicks = startTime.Ticks;
            var endTicks = endTime.Ticks;
            var data = ProjectionHelper.Work(rows, startTicks, endTicks, numberBuckets);

            int[] totalCounts = new int[numberBuckets];
            double bucketWidthTicks = ((double)(endTicks - startTicks)) / numberBuckets;
            foreach (var row in rows)
            {
                int idx = (int) ((row.GetTicks() - startTicks) / bucketWidthTicks);
                if (idx >= 0 && idx < numberBuckets)
                {
                    totalCounts[idx] += row.TotalThisPeriod;
                }
            }

            // coerce data            
            var chart = new FunctionVolumeTimelineEntry[numberBuckets];
            for (int i = 0; i < numberBuckets; i++)
            {
                var ticks = data[i].Item1;
                var time = new DateTime(ticks);
                double value = data[i].Item2;
                chart[i] = new FunctionVolumeTimelineEntry
                {
                    Time = time,
                    Volume = value,
                    InstanceCounts = totalCounts[i]
                };
            }

            return chart;
        }

        public async Task<Segment<IFunctionDefinition>> GetFunctionDefinitionsAsync(string continuationToken)
        {
            var query = TableScheme.GetRowsInPartition<FunctionDefinitionEntity>(TableScheme.FuncDefIndexPK);
            var results = await _instanceTable.SafeExecuteQueryAsync(query);

            DateTime min = DateTime.MinValue;
            foreach (var entity in results)
            {
                if (entity.Timestamp > min)
                {
                    min = entity.Timestamp.DateTime;
                }
            }
            
            var segment = new Segment<IFunctionDefinition>(results);
            return segment;
        }

        // Lookup a single instance by id. 
        public async Task<FunctionInstanceLogItem> LookupFunctionInstanceAsync(Guid id)
        {
            // Create a retrieve operation that takes a customer entity.
            TableOperation retrieveOperation = InstanceTableEntity.GetRetrieveOperation(id);

            // Execute the retrieve operation.
            TableResult retrievedResult = await _instanceTable.SafeExecuteAsync(retrieveOperation);

            var x = (InstanceTableEntity)retrievedResult.Result;

            if (x == null)
            {
                return null;
            }
            return x.ToFunctionLogItem();
        }

        public async Task<Segment<ActivationEvent>> GetActiveContainerTimelineAsync(DateTime start, DateTime end, string continuationToken)
        {
            var query = ContainerActiveEntity.GetQuery(start, end);
            var results = await _instanceTable.SafeExecuteQueryAsync(query);
            
            List<ActivationEvent> l = new List<ActivationEvent>();
            Dictionary<string, string> intern = new Dictionary<string, string>();

            foreach (var result in results)
            {
                var name = result.GetContainerName();
                string internedName;
                if (!intern.TryGetValue(name, out internedName))
                {
                    intern[name] = name;
                    internedName = name;
                }

                var timeBucket = result.GetStartBucket();
                l.Add(new ActivationEvent
                {
                    ContainerName = name,
                    StartTimeBucket = timeBucket,
                    StartTime  = TimeBucket.ConvertToDateTime(timeBucket),
                    Length = result.GetLength()
                });
            }

            return new Segment<ActivationEvent>(l.ToArray(), null);
        }

        public async Task<Segment<IAggregateEntry>> GetAggregateStatsAsync(string functionName, DateTime start, DateTime end, string continuationToken)
        {
            if (functionName == null)
            {
                throw new ArgumentNullException("functionName");
            }
            if (start > end)
            {
                throw new ArgumentOutOfRangeException("start");
            }
            var rangeQuery = TimelineAggregateEntity.GetQuery(functionName, start, end);
            var results = await _instanceTable.SafeExecuteQueryAsync(rangeQuery);

            return new Segment<IAggregateEntry>(results);
        }

        // Could be very long 
        public async Task<Segment<IRecentFunctionEntry>> GetRecentFunctionInstancesAsync(
            RecentFunctionQuery queryParams,
            string continuationToken)
        {
            TableQuery<RecentPerFuncEntity> rangeQuery = RecentPerFuncEntity.GetRecentFunctionsQuery(queryParams);

            CancellationToken cancellationToken;
            TableContinuationToken realContinuationToken = Utility.DeserializeToken(continuationToken);
            var segment = await _instanceTable.SafeExecuteQuerySegmentedAsync<RecentPerFuncEntity>(
                rangeQuery, 
                realContinuationToken, 
                cancellationToken);

            if (segment == null)
            {
                return new Segment<IRecentFunctionEntry>(new IRecentFunctionEntry[0]);
            }
            else
            {
                return new Segment<IRecentFunctionEntry>(segment.Results.ToArray(), Utility.SerializeToken(segment.ContinuationToken));
            }
        }
    }
}