using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Caching;
using Dashboard.Data;
using Dashboard.ViewModels;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.InvocationLog
{
    /// <summary>
    /// A facade for loading invocations using seconday-index tables
    /// For e.g., loading recent invocations, invocations in a job, a function etc.
    /// </summary>
    internal class InvocationLogLoader : IInvocationLogLoader
    {
        private readonly IFunctionInvocationIndexReader _invocationsInJobReader;
        private readonly IFunctionInvocationIndexReader _invocationChildrenReader;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;
        private readonly IHeartbeatMonitor _heartbeatMonitor;

        public InvocationLogLoader(
            IFunctionInvocationIndexReader invocationsInJobReader,
            IFunctionInvocationIndexReader invocationChildrenReader, 
            IFunctionInstanceLookup functionInstanceLookup,
            IHeartbeatMonitor heartbeatMonitor)
        {
            _invocationsInJobReader = invocationsInJobReader;
            _invocationChildrenReader = invocationChildrenReader;
            _functionInstanceLookup = functionInstanceLookup;
            _heartbeatMonitor = heartbeatMonitor;
        }

        public InvocationLogPage GetInvocationChildren(Guid invocationId, PagingInfo pagingInfo)
        {
            return GetPage(_invocationChildrenReader, invocationId.ToString(), pagingInfo);
        }

        public InvocationLogPage GetInvocationsInJob(string jobKey, PagingInfo pagingInfo)
        {
            return GetPage(_invocationsInJobReader, jobKey, pagingInfo);
        }

        public InvocationLogViewModel[] GetInvocationsByIds(IEnumerable<Guid> invocationIds)
        {
            var invocationLogs = from invocationId in invocationIds.AsParallel()
                let invocationModel = GetInvocationLogViewModel(invocationId)
                where invocationModel != null
                select invocationModel;

            return invocationLogs.ToArray();
        }

        private InvocationLogPage GetPage(IFunctionInvocationIndexReader reader, string partitionKey, PagingInfo pagingInfo)
        {
            pagingInfo.Limit = pagingInfo.Limit ?? 20;
            var indexEntries = reader.Query(partitionKey, pagingInfo.OlderThan, pagingInfo.OlderThanOrEqual, pagingInfo.NewerThan, pagingInfo.Limit + 1);

            bool hasMore = indexEntries.Length > pagingInfo.Limit.Value;

            var invocationLogs = from indexEntry in indexEntries.Take(pagingInfo.Limit.Value).AsParallel()
                let invocationModel = GetInvocationLogViewModel(indexEntry.InvocationId)
                where invocationModel != null
                select new SortableInvocationEntry {Invocation = invocationModel, Key = indexEntry.RowKey};

            return new InvocationLogPage
            {
                Entries = invocationLogs.OrderBy(e => e.Key).ToArray(),
                HasMore = hasMore
            };
        }

        private InvocationLogViewModel GetInvocationLogViewModel(Guid invocationId)
        {
            return GetInvocationLogViewModel(_functionInstanceLookup, _heartbeatMonitor, invocationId);
        }

        internal static InvocationLogViewModel GetInvocationLogViewModel(IFunctionInstanceLookup functionInstanceLookup,
            IHeartbeatMonitor heartbeatMonitor, Guid invocationId)
        {
            InvocationLogViewModel invocationModel = null;
            string cacheKey = "INVOCATION_MODEL_" + invocationId;
            if (HttpRuntime.Cache != null)
            {
                invocationModel = HttpRuntime.Cache.Get(cacheKey) as InvocationLogViewModel;
                if (invocationModel == null)
                {
                    var invocation = functionInstanceLookup.Lookup(invocationId);
                    if (invocation != null)
                    {
                        invocationModel = new InvocationLogViewModel(invocation, HasValidHeartbeat(heartbeatMonitor, invocation));
                        if (invocationModel.IsFinal())
                        {
                            HttpRuntime.Cache.Insert(cacheKey, invocationModel, null, Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(30));
                        }
                    }
                }
            }
            else
            {
                var invocation = functionInstanceLookup.Lookup(invocationId);
                if (invocation != null)
                {
                    invocationModel = new InvocationLogViewModel(invocation, HasValidHeartbeat(heartbeatMonitor, invocation));
                }
            }

            return invocationModel;
        }

        private static bool? HasValidHeartbeat(IHeartbeatMonitor heartbeatMonitor, FunctionInstanceSnapshot snapshot)
        {
            HeartbeatDescriptor heartbeat = snapshot.Heartbeat;

            if (heartbeat == null)
            {
                return null;
            }

            return heartbeatMonitor.IsInstanceHeartbeatValid(heartbeat.SharedContainerName,
                heartbeat.SharedDirectoryName, heartbeat.InstanceBlobName, heartbeat.ExpirationInSeconds);
        }
    }
}
