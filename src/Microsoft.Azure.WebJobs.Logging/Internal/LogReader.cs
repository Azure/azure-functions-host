// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

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
            table.CreateIfNotExists();
            this._instanceTable = table;
        }

        public Task<string[]> GetFunctionNamesAsync()
        {
            var query = TableScheme.GetRowsInPartition<FunctionDefinitionEntity>(TableScheme.FuncDefIndexPK);
            var results = _instanceTable.ExecuteQuery(query).ToArray();

            var functionNames = Array.ConvertAll(results, entity => entity.GetFunctionName());

            return Task.FromResult(functionNames);
        }

        // Lookup a single instance by id. 
        public async Task<FunctionInstanceLogItem> LookupFunctionInstanceAsync(Guid id)
        {
            // Create a retrieve operation that takes a customer entity.
            TableOperation retrieveOperation = InstanceTableEntity.GetRetrieveOperation(id);

            // Execute the retrieve operation.
            TableResult retrievedResult = await _instanceTable.ExecuteAsync(retrieveOperation);

            var x = (InstanceTableEntity)retrievedResult.Result;

            if (x == null)
            {
                return null;
            }
            return x.ToFunctionLogItem();
        }

        public Task<Segment<ActivationEvent>> GetActiveContainerTimelineAsync(DateTime start, DateTime end, string continuationToken)
        {
            var query = ContainerActiveEntity.GetQuery(start, end);
            var results = _instanceTable.ExecuteQuery(query).ToArray();
            
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

            return Task.FromResult(new Segment<ActivationEvent>(l.ToArray(), null));
        }

        public Task<Segment<IAggregateEntry>> GetAggregateStatsAsync(string functionName, DateTime start, DateTime end, string continuationToken)
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
            var results = _instanceTable.ExecuteQuery(rangeQuery).ToArray();

            return Task.FromResult(new Segment<IAggregateEntry>(results));
        }

        // Could be very long 
        public async Task<Segment<IRecentFunctionEntry>> GetRecentFunctionInstancesAsync(
            RecentFunctionQuery queryParams,
            string continuationToken)
        {
            TableQuery<RecentPerFuncEntity> rangeQuery = RecentPerFuncEntity.GetRecentFunctionsQuery(queryParams);

            CancellationToken cancellationToken;
            TableContinuationToken realContinuationToken = Utility.DeserializeToken(continuationToken); ;
            var segment = await _instanceTable.ExecuteQuerySegmentedAsync<RecentPerFuncEntity>(
                rangeQuery, 
                realContinuationToken, 
                cancellationToken);

            return new Segment<IRecentFunctionEntry>(segment.Results.ToArray(), Utility.SerializeToken(segment.ContinuationToken));
        }
    }
}