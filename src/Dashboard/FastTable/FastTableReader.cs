// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    // Adapter to plug in fast tables to Dashboard
    internal class FastTableReader :
        IFunctionLookup,
        IFunctionInstanceLookup,
        IFunctionStatisticsReader,
        IRecentInvocationIndexReader,
        IRecentInvocationIndexByFunctionReader,
        IFunctionIndexReader
    {
        // Underlying reader. 
        private readonly ILogReader _reader;

        DateTimeOffset _version = DateTimeOffset.UtcNow;

        private FunctionSnapshot[] _snapshots = null; // Cache of function definitions 

        public FastTableReader(CloudTable table)
        {
            _reader = new LogReader(table);
        }

        private async Task<FunctionSnapshot[]> GetSnapshotsAsync()
        {
            if (_snapshots == null)
            {
                var results = await _reader.GetFunctionDefinitionsAsync();

                _snapshots = Array.ConvertAll(results, x => new FunctionSnapshot
                {
                    Id = x.RowKey,
                    FullName = x.RowKey,
                    ShortName = x.RowKey,
                    Parameters = new Dictionary<string, ParameterSnapshot>()
                });
            }
            return _snapshots;
        }

        FunctionInstanceSnapshot IFunctionInstanceLookup.Lookup(Guid id)
        {
            var theTask = Task.Run(() => LookupAsync(id));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        async Task<FunctionInstanceSnapshot> LookupAsync(Guid id)
        {
            var t = await _reader.LookupFunctionInstanceAsync(id);
            return t.ConvertToSnapshot();
        }

        FunctionStatistics IFunctionStatisticsReader.Lookup(string functionId)
        {
            var theTask = Task.Run(() => LookupAsync(functionId));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }
        private async Task<FunctionStatistics> LookupAsync(string functionId)
        {
            var total = new FunctionStatistics
            {
            };

            // summarize over last 7 days. 
            DateTime now = DateTime.UtcNow;
            DateTime start = now.AddDays(-7);

            var items = await _reader.GetAggregateStatsAsync(functionId, start, now);

            foreach (var item in items)
            {
                total.SucceededCount += item.TotalPass;
                total.FailedCount += item.TotalFail;
            }

            return total;
        }

        FunctionSnapshot IFunctionLookup.Read(string functionId)
        {
            var theTask = Task.Run(() => ReadAsync(functionId));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        private async Task<FunctionSnapshot> ReadAsync(string functionId)
        {
            var snapshots = await GetSnapshotsAsync();
            var snapshot = snapshots.FirstOrDefault(x => x.Id == functionId);
            return snapshot;
        }

        public DateTimeOffset GetCurrentVersion()
        {
            return _version;
        }

        IResultSegment<FunctionIndexEntry> IFunctionIndexReader.Read(int maximumResults, string continuationToken)
        {
            var theTask = Task.Run(() => Read1Async(maximumResults, continuationToken));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        async Task<IResultSegment<FunctionIndexEntry>> Read1Async(int maximumResults, string continuationToken)
        {
            var snapshots = await GetSnapshotsAsync();
            var results = Array.ConvertAll(snapshots, x =>
                  FunctionIndexEntry.Create(
                        FunctionIndexEntry.CreateOtherMetadata(x), _version)
              );

            return new ResultSegment<FunctionIndexEntry>(results, null);
        }

        IResultSegment<RecentInvocationEntry> IRecentInvocationIndexReader.Read(int maximumResults, string continuationToken)
        {
            var theTask = Task.Run(() => Read2Async(maximumResults, continuationToken));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        private async Task<IResultSegment<RecentInvocationEntry>> Read2Async(int maximumResults, string continuationToken)
        {
            var snapshots = await GetSnapshotsAsync();

            string[] functionNames = Array.ConvertAll(snapshots, x => x.Id);

            Task<IQueryResults<RecentPerFuncEntity>>[] queryTasks = Array.ConvertAll(functionNames, functionName => _reader.GetRecentFunctionInstancesAsync(functionName));
            IQueryResults<RecentPerFuncEntity>[] queries = await Task.WhenAll(queryTasks);

            Task<RecentPerFuncEntity[]>[] batchesTasks = Array.ConvertAll(queries, query => query.GetNextAsync(100));
            RecentPerFuncEntity[][] results = await Task.WhenAll(batchesTasks);

            // Merge and sort 
            List<RecentPerFuncEntity> flatListEntities = new List<RecentPerFuncEntity>();
            foreach (var set in results)
            {
                flatListEntities.AddRange(set);
            }
            var entityValues = flatListEntities.ToArray();
            var keys = Array.ConvertAll(entityValues, x => DescendingTime(x.StartTime)); // sort descending by time, most recent at top
            Array.Sort(keys, entityValues);

            entityValues = entityValues.Take(maximumResults).ToArray();

            var finalValues = Array.ConvertAll(entityValues, entity => Convert(entity));

            return new ResultSegment<RecentInvocationEntry>(finalValues, null);
        }

        static RecentInvocationEntry Convert(RecentPerFuncEntity entity)
        {
            var snapshot = entity.ConvertToSnapshot();
            var metadata = RecentInvocationEntry.CreateMetadata(snapshot);
            return RecentInvocationEntry.Create(metadata);
        }

        static long DescendingTime(DateTimeOffset d)
        {
            return long.MaxValue - d.Ticks;
        }

        IResultSegment<RecentInvocationEntry> IRecentInvocationIndexByFunctionReader.Read(string functionId, int maximumResults, string continuationToken)
        {
            var theTask = Task.Run(() => Read3Async(functionId, maximumResults, continuationToken));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        private async Task<IResultSegment<RecentInvocationEntry>> Read3Async(string functionId, int maximumResults, string continuationToken)
        {
            var x = await _reader.GetRecentFunctionInstancesAsync(functionId);

            RecentPerFuncEntity[] batch = await x.GetNextAsync(1000);

            batch = batch.Take(maximumResults).ToArray();

            var results = Array.ConvertAll(batch, item =>
            RecentInvocationEntry.Create(RecentInvocationEntry.CreateMetadata(item.ConvertToSnapshot())));

            return new ResultSegment<RecentInvocationEntry>(results, null);
        }
    }
}