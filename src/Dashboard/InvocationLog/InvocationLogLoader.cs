using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Caching;
using Dashboard.ViewModels;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// A facade for loading invocations using seconday-index tables
    /// For e.g., loading recent invocations, invocations in a job, a function etc.
    /// </summary>
    internal class InvocationLogLoader : IInvocationLogLoader
    {
        private readonly IFunctionInvocationIndexReader _invocationsInJobReader;
        private readonly IFunctionInvocationIndexReader _invocationsInFunctionReader;
        private readonly IFunctionInvocationIndexReader _recentInvocationsReader;
        private readonly IFunctionInvocationIndexReader _invocationChildrenReader;
        private readonly IFunctionInstanceLookup _functionInstanceLookup;

        public InvocationLogLoader(
            IFunctionInvocationIndexReader invocationsInJobReader,
            IFunctionInvocationIndexReader invocationsInFunctionReader,
            IFunctionInvocationIndexReader recentInvocationsReader,
            IFunctionInvocationIndexReader invocationChildrenReader, 
            IFunctionInstanceLookup functionInstanceLookup)
        {
            _invocationsInJobReader = invocationsInJobReader;
            _invocationsInFunctionReader = invocationsInFunctionReader;
            _recentInvocationsReader = recentInvocationsReader;
            _invocationChildrenReader = invocationChildrenReader;
            _functionInstanceLookup = functionInstanceLookup;
        }

        public InvocationLogPage GetInvocationChildren(Guid invocationId, PagingInfo pagingInfo)
        {
            return GetPage(_invocationChildrenReader, invocationId.ToString(), pagingInfo);
        }

        public InvocationLogPage GetRecentInvocations(PagingInfo pagingInfo)
        {
            return GetPage(_recentInvocationsReader, "1", pagingInfo);
        }

        public InvocationLogPage GetInvocationsInFunction(string functionId, PagingInfo pagingInfo)
        {
            return GetPage(_invocationsInFunctionReader, functionId, pagingInfo);
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

        InvocationLogViewModel GetInvocationLogViewModel(Guid invocationId)
        {
            InvocationLogViewModel invocationModel = null;
            string cacheKey = "INVOCATION_MODEL_" + invocationId;
            if (HttpRuntime.Cache != null)
            {
                invocationModel = HttpRuntime.Cache.Get(cacheKey) as InvocationLogViewModel;
                if (invocationModel == null)
                {
                    var invocation = _functionInstanceLookup.Lookup(invocationId);
                    if (invocation != null)
                    {
                        invocationModel = new InvocationLogViewModel(invocation);
                        if (invocationModel.IsFinal())
                        {
                            HttpRuntime.Cache.Insert(cacheKey, invocationModel, null, Cache.NoAbsoluteExpiration, TimeSpan.FromMinutes(30));
                        }
                    }
                }
            }
            else
            {
                var invocation = _functionInstanceLookup.Lookup(invocationId);
                if (invocation != null)
                {
                    invocationModel = new InvocationLogViewModel(invocation);
                }
            }

            return invocationModel;
        }
    }
}