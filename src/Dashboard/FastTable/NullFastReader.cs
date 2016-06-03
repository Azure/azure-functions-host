// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;

namespace Dashboard
{
    // USed in non-fast paths. 
    internal class NullFastReader : ILogReader
    {
        public Task<Segment<ActivationEvent>> GetActiveContainerTimelineAsync(DateTime startTime, DateTime endTime, string continuationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Segment<IAggregateEntry>> GetAggregateStatsAsync(string functionName, DateTime startTime, DateTime endTime, string continuationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Segment<IFunctionDefinition>> GetFunctionDefinitionsAsync(string continuationToken)
        {
            throw new NotImplementedException();
        }

        public Task<Segment<IRecentFunctionEntry>> GetRecentFunctionInstancesAsync(RecentFunctionQuery query, string continuationToken)
        {
            throw new NotImplementedException();
        }

        public Task<FunctionInstanceLogItem> LookupFunctionInstanceAsync(Guid functionInstanceId)
        {
            throw new NotImplementedException();
        }

        public Task<FunctionVolumeTimelineEntry[]> GetVolumeAsync(DateTime startTime, DateTime endTime, int numBuckets)
        {
            throw new NotImplementedException();
        }
    }
}