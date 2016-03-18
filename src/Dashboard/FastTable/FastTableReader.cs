// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
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

        private DateTimeOffset _version = DateTimeOffset.UtcNow;

        private FunctionSnapshot[] _snapshots = null; // Cache of function definitions 

        public FastTableReader(CloudTable table)
        {
            _reader = LogFactory.NewReader(table);
        }

        public FastTableReader(ILogReader reader)
        {
            _reader = reader;
        }

        private async Task<FunctionSnapshot[]> GetSnapshotsAsync()
        {
            if (_snapshots == null)
            {
                string[] results = await _reader.GetFunctionNamesAsync();

                _snapshots = Array.ConvertAll(results, name => new FunctionSnapshot
                {
                    Id = name,
                    FullName = name,
                    ShortName = name,
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

        private async Task<FunctionInstanceSnapshot> LookupAsync(Guid id)
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

            var segment = await _reader.GetAggregateStatsAsync(functionId, start, now, null);
            var items = segment.Results;

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

        DateTimeOffset IFunctionIndexReader.GetCurrentVersion()
        {
            return _version;
        }

        IResultSegment<FunctionIndexEntry> IFunctionIndexReader.Read(int maximumResults, string continuationToken)
        {
            var theTask = Task.Run(() => Read1Async());
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        private async Task<IResultSegment<FunctionIndexEntry>> Read1Async()
        {
            var snapshots = await GetSnapshotsAsync();
            var results = Array.ConvertAll(snapshots, x =>
                  FunctionIndexEntry.Create(
                        FunctionIndexEntry.CreateOtherMetadata(x), _version));

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
            var endTime = FromContinuationToken(continuationToken, DateTime.MaxValue);
            var snapshots = await GetSnapshotsAsync();

            string[] functionNames = Array.ConvertAll(snapshots, x => x.Id);

            Task<Segment<IRecentFunctionEntry>>[] queryTasks = Array.ConvertAll(
                functionNames,
                functionName => _reader.GetRecentFunctionInstancesAsync(new RecentFunctionQuery
                {
                    FunctionName = functionName,
                    MaximumResults = 100,
                    Start = DateTime.MinValue, 
                    End = endTime                    
                }, null));

            Segment<IRecentFunctionEntry>[] queries = await Task.WhenAll(queryTasks);

            IRecentFunctionEntry[][] results = Array.ConvertAll(queries, query => query.Results);

            // Merge and sort 
            List<IRecentFunctionEntry> flatListEntities = new List<IRecentFunctionEntry>();
            foreach (var set in results)
            {
                flatListEntities.AddRange(set);
            }
            var entityValues = flatListEntities.ToArray();
            var keys = Array.ConvertAll(entityValues, x => DescendingTime(x.StartTime)); // sort descending by time, most recent at top
            Array.Sort(keys, entityValues);

            int available = entityValues.Length;
            entityValues = entityValues.Take(maximumResults).ToArray();

            string continuationToken2 = null;
            if (available > maximumResults)
            {
                var lastDate = entityValues[entityValues.Length - 1].StartTime.DateTime;
                lastDate.AddTicks(-1); // Descending timescale. 
                continuationToken2 = ToContinuationToken(lastDate);
            }

            var finalValues = Array.ConvertAll(entityValues, entity => Convert(entity));

            return new ResultSegment<RecentInvocationEntry>(finalValues, continuationToken2);
        }

        private static RecentInvocationEntry Convert(IRecentFunctionEntry entity)
        {
            var snapshot = entity.ConvertToSnapshot();
            var metadata = RecentInvocationEntry.CreateMetadata(snapshot);
            return RecentInvocationEntry.Create(metadata);
        }

        private static long DescendingTime(DateTimeOffset d)
        {
            return long.MaxValue - d.Ticks;
        }

        IResultSegment<RecentInvocationEntry> IRecentInvocationIndexByFunctionReader.Read(string functionId, int maximumResults, string continuationToken)
        {
            var theTask = Task.Run(() => Read3Async(functionId, maximumResults, continuationToken));
            var retVal = theTask.GetAwaiter().GetResult();
            return retVal;
        }

        private async Task<IResultSegment<RecentInvocationEntry>> Read3Async(
            string functionId, int maximumResults, string continuationToken)
        {
            var queryParams = new RecentFunctionQuery
            {
                FunctionName = functionId,
                MaximumResults = maximumResults,
                Start = DateTime.MinValue,
                End = DateTime.MaxValue
            };

            var segment = await _reader.GetRecentFunctionInstancesAsync(queryParams, continuationToken);

            var results = Array.ConvertAll(segment.Results, item =>
                RecentInvocationEntry.Create(RecentInvocationEntry.CreateMetadata(item.ConvertToSnapshot())));

            return new ResultSegment<RecentInvocationEntry>(results, segment.ContinuationToken);
        }

        private static string ToContinuationToken(DateTime time)
        {
            return time.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private static DateTime FromContinuationToken(string continuationToken, DateTime defaultValue)
        {
            if (continuationToken == null)
            {
                return defaultValue;
            }
            long ticks = long.Parse(continuationToken, CultureInfo.InvariantCulture);
            return new DateTime(ticks);
        }
    }
}