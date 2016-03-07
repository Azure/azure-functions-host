// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Create a reader. 
    public class LogReader : ILogReader
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

        public async Task<FunctionDefinitionEntity[]> GetFunctionDefinitionsAsync()
        {
            var query = TableScheme.GetRowsInPartition<FunctionDefinitionEntity>(TableScheme.FuncDefIndexPK);
            var results = _instanceTable.ExecuteQuery(query).ToArray();

            return results;
        }

        // Lookup a single instance by id. 
        public async Task<InstanceTableEntity> LookupFunctionInstanceAsync(Guid id)
        {
            // Create a retrieve operation that takes a customer entity.
            TableOperation retrieveOperation = InstanceTableEntity.GetRetrieveOperation(id);

            // Execute the retrieve operation.
            TableResult retrievedResult = _instanceTable.Execute(retrieveOperation);

            var x = (InstanceTableEntity)retrievedResult.Result;
            return x;
        }

        public async Task<ActivationEvent[]> GetActiveContainerCountOverTimeAsync(DateTime start, DateTime end)
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

                l.Add(new ActivationEvent
                {
                    ContainerName = name,
                    Start = result.GetStartBucket(),
                    Length = result.GetLength()
                });
            }

            return l.ToArray();
        }

        public async Task<TimelineAggregateEntity[]> GetAggregateStatsAsync(string functionName, DateTime start, DateTime end)
        {
            var rangeQuery = TimelineAggregateEntity.GetQuery(functionName, start, end);
            var results = _instanceTable.ExecuteQuery(rangeQuery).ToArray();

            return results;
        }

        class RecentFuncQueryResult : IQueryResults<RecentPerFuncEntity>
        {
            internal CloudTable _instanceTable;
            internal TableQuery<RecentPerFuncEntity> _query;
            private TableContinuationToken _continue;
            private bool _done;

            public async Task<RecentPerFuncEntity[]> GetNextAsync(int limit)
            {
                if (_done)
                {
                    return null;
                }
                CancellationToken cancellationToken;
                
                var segment = await _instanceTable.ExecuteQuerySegmentedAsync<RecentPerFuncEntity>(_query, _continue, cancellationToken);

                var results = segment.Results;

                _continue = segment.ContinuationToken; // End of query? 
                if (_continue == null)
                {
                    _done = true;
                } else if (results == null)
                {
                    return new RecentPerFuncEntity[0]; 
                }
                return results.ToArray();
            }
        }

        // Could be very long 
        public async Task<IQueryResults<RecentPerFuncEntity>> GetRecentFunctionInstancesAsync(
            string functionName, 
            bool onlyFailures)
        {
            TableQuery<RecentPerFuncEntity> rangeQuery = RecentPerFuncEntity.GetRecentFunctionsQuery(functionName);

            var q = new RecentFuncQueryResult
            {
                _instanceTable = this._instanceTable,
                _query = rangeQuery
            };
            return q;
        }
    }
}