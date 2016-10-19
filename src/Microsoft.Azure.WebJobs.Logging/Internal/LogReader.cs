// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
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
        // Writes go to table storage. They're split across tables based on their date. 
        private readonly ILogTableProvider _tableLookup;

        public LogReader(ILogTableProvider tableLookup)
        {
            if (tableLookup == null)
            {
                throw new ArgumentNullException("tableLookup");
            }
            this._tableLookup = tableLookup;
        }

        public async Task<FunctionVolumeTimelineEntry[]> GetVolumeAsync(DateTime startTime, DateTime endTime, int numberBuckets)
        {
            var query = InstanceCountEntity.GetQuery(startTime, endTime);

            var iter = await EpochTableIterator.NewAsync(_tableLookup);
            var results = await iter.SafeExecuteQuerySegmentedAsync<InstanceCountEntity>(query, startTime, endTime);

            InstanceCountEntity[] rows = results.Results;

            var startTicks = startTime.Ticks;
            var endTicks = endTime.Ticks;
            var data = ProjectionHelper.Work(rows, startTicks, endTicks, numberBuckets);

            int[] totalCounts = new int[numberBuckets];
            double bucketWidthTicks = ((double)(endTicks - startTicks)) / numberBuckets;
            foreach (var row in rows)
            {
                int idx = (int)((row.GetTicks() - startTicks) / bucketWidthTicks);
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
            var instanceTable = _tableLookup.GetTableForDateTime(TimeBucket.CommonEpoch);
            var results = await GetFunctionDefinitionsHelperAsync(instanceTable);

            var legacyTable = LegacyTableReader.GetLegacyTable(_tableLookup);
            if (legacyTable != null)
            {
                var olderResults = await GetFunctionDefinitionsHelperAsync(legacyTable);
                results = LegacyTableReader.Merge(results, olderResults);
            }

            var segment = new Segment<IFunctionDefinition>(results);
            return segment;
        }

        private async Task<IFunctionDefinition[]> GetFunctionDefinitionsHelperAsync(CloudTable table)
        {
            var query = TableScheme.GetRowsInPartition<FunctionDefinitionEntity>(TableScheme.FuncDefIndexPK);
            var results = await table.SafeExecuteQueryAsync(query);
                    
            return results;          
        }

        // Lookup a single instance by id. 
        public async Task<FunctionInstanceLogItem> LookupFunctionInstanceAsync(Guid id)
        {
            var tables = await _tableLookup.ListTablesAsync();

            Task<FunctionInstanceLogItem>[] taskLookups = Array.ConvertAll(tables, async instanceTable =>
            {
                // Create a retrieve operation that takes a customer entity.
                TableOperation retrieveOperation = InstanceTableEntity.GetRetrieveOperation(id);

                // Execute the retrieve operation.
                TableResult retrievedResult = await instanceTable.SafeExecuteAsync(retrieveOperation);

                var entity = (InstanceTableEntity)retrievedResult.Result;

                if (entity == null)
                {
                    return null;
                }
                return entity.ToFunctionLogItem();
            });

            FunctionInstanceLogItem[] results = await Task.WhenAll(taskLookups);
            foreach (var result in results)
            {
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        public async Task<Segment<ActivationEvent>> GetActiveContainerTimelineAsync(DateTime start, DateTime end, string continuationToken)
        {
            var query = ContainerActiveEntity.GetQuery(start, end);

            var iter = await EpochTableIterator.NewAsync(_tableLookup);
            var segment = await iter.SafeExecuteQuerySegmentedAsync<ContainerActiveEntity>(
                query, start, end);

            var results = segment.Results;
            
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

            var iter = await EpochTableIterator.NewAsync(_tableLookup);

            var rangeQuery = TimelineAggregateEntity.GetQuery(functionName, start, end);

            var results = await iter.SafeExecuteQuerySegmentedAsync<TimelineAggregateEntity>(rangeQuery, start, end);

            return results.As<IAggregateEntry>();
        }

        // Could be very long 
        public async Task<Segment<IRecentFunctionEntry>> GetRecentFunctionInstancesAsync(
            RecentFunctionQuery queryParams,
            string continuationToken)
        {
            TableQuery<RecentPerFuncEntity> rangeQuery = RecentPerFuncEntity.GetRecentFunctionsQuery(queryParams);

            var iter = await EpochTableIterator.NewAsync(_tableLookup);
            var results = await iter.SafeExecuteQuerySegmentedAsync<RecentPerFuncEntity>(rangeQuery, queryParams.Start, queryParams.End);

            return results.As<IRecentFunctionEntry>();
        }

        // Helper to run queries which can span multiple tables.         
        private class EpochTableIterator
        {
            private readonly Dictionary<long, CloudTable> _tables; // map of epoch to physical tables.

            private EpochTableIterator(Dictionary<long, CloudTable> tables)
            {
                _tables = tables;
            }
            
            public static async Task<EpochTableIterator> NewAsync(ILogTableProvider tableLookup)
            {
                Dictionary <long, CloudTable> d = new Dictionary<long, CloudTable>();

                var tables = await tableLookup.ListTablesAsync();

                foreach (var table in tables)
                {
                    var epoch = TimeBucket.GetEpochNumberFromTable(table);
                    d[epoch] = table;
                }
                return new EpochTableIterator(d);
            }

            // Given an epoch, find the next one in physicalEpochs that it would fall into, using reverse-chronological order.              
            // physicalEpochs - epochs for existing tables, sorted in ascending order.  This is generally short (expected under 50 elements)
            // Returns a physical epoch (from the list passed in) that the requested epoch falls into 
            // or -1 if there's no physical epoch below this.
            private static long FindNext(long epoch, long[] physicalEpochs)
            {
                // start at most recent epoch and work backwards 
                for (var i = physicalEpochs.Length - 1; i >= 0; i--)
                {
                    if (epoch >= physicalEpochs[i])
                    {
                        return physicalEpochs[i];
                    }
                }

                return -1; // done 
            }

            // Helper workaround for this bug: 
            // https://github.com/projectkudu/AzureFunctionsPortal/issues/634 
            // Portal needs to be updated to use continuation tokens. Once it is, we can remove this shim
            // and let the portal drive the enumeration directly. 
            public async Task<Segment<TElement>> SafeExecuteQuerySegmentedAsync<TElement>(
                TableQuery<TElement> rangeQuery,
                DateTime startTime, // Range for query 
                DateTime endTime
            ) where TElement : ITableEntity, new()
            {
                List<TElement> list = new List<TElement>();

                string continuationToken = null;
                while (true)
                {
                    var segment = await SafeExecuteQuerySegmentedAsync(rangeQuery, startTime, endTime, continuationToken);

                    list.AddRange(segment.Results);
                    if (segment.ContinuationToken == null)
                    {
                        // Done!
                        return new Segment<TElement>(list.ToArray(), null);
                    }
                    continuationToken = segment.ContinuationToken;
                }
            }
            
            // Date range queries start with most recent (endTime) and then return entities in descending chronological order. 
            public async Task<Segment<TElement>> SafeExecuteQuerySegmentedAsync<TElement>(
                TableQuery<TElement> rangeQuery,
                DateTime startTime, // Range for query 
                DateTime endTime,
                string continuationToken
                ) where TElement : ITableEntity, new()
            {
                if (endTime < startTime)
                {
                    throw new InvalidOperationException("illegal time range");
                }

                // Shrink to phsyical. 
                var epochs = _tables.Keys.ToArray();
                if (epochs.Length == 0)
                {
                    return new Segment<TElement>(new TElement[0]);
                }
                Array.Sort(epochs);

                List<TElement> items = new List<TElement>();

                // Incoming cursor state. 
                TableContinuationToken realContinuationToken;
                long currentEpoch;
                DecodeContinuationToken(continuationToken, out realContinuationToken, out currentEpoch);
                currentEpoch = FindNext(currentEpoch, epochs);

                CloudTable table;
                TableQuerySegment<TElement> segment = null;

                if (_tables.TryGetValue(currentEpoch, out table))
                {
                    segment = await table.SafeExecuteQuerySegmentedAsync<TElement>(
                        rangeQuery,
                        realContinuationToken,
                        CancellationToken.None);

                    // segment will be null if table doesn't exist. 
                    // That shouldn't happen normally since we're looking at physical tables.
                    // But it could happen in a maniacal case is the table got deleted. 
                    if (segment != null)
                    {
                        items.AddRange(segment.Results);

                        realContinuationToken = segment.ContinuationToken;
                        if (segment.ContinuationToken == null)
                        {
                            // We're done querying the current table. 
                            // Move down to the next table. 
                            segment = null;
                        }

                    }
                }
                else
                {
                    // This shouldn't happen since our list since FindNext() should have left us at a physical epoch.
                    // Just fall through and treat it the same as if the table doesn't exist. 
                }

                if (segment == null)
                {
                    // We're done querying the current table. 
                    // It doesn't matter whether we finished querying normally or table doesn't exist
                    // Move down to the next table. 
                    currentEpoch--;
                }

                string nextContinuationToken = null;
                var next = FindNext(currentEpoch, epochs);
                if (next >= 0)
                {
                    nextContinuationToken = MakeContinuationToken(currentEpoch, realContinuationToken);
                }

                return new Segment<TElement>(items.ToArray(), nextContinuationToken);
            }

            private static string MakeContinuationToken(long currentEpoch, TableContinuationToken realContinuationToken)
            {
                return currentEpoch.ToString(CultureInfo.InvariantCulture) + "|" + Utility.SerializeToken(realContinuationToken);
            }

            private static void DecodeContinuationToken(string continuationToken, out TableContinuationToken realContinuationToken, out long currentEpoch)
            {
                if (continuationToken == null)
                {
                    realContinuationToken = null;
                    currentEpoch = long.MaxValue;
                    return;
                }
                var split = continuationToken.Split('|');
                if (split.Length != 2)
                {
                    throw new InvalidOperationException("Bad continuation token");
                }
                currentEpoch = long.Parse(split[0], CultureInfo.InvariantCulture);
                realContinuationToken = Utility.DeserializeToken(split[1]);
            }
        }
    }
}