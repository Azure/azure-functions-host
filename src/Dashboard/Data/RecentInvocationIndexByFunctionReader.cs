using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByFunctionReader : IRecentInvocationIndexByFunctionReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexByFunctionReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client))
        {
        }

        private RecentInvocationIndexByFunctionReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(string functionId, int maximumResults,
            string continuationToken)
        {
            string prefix = DashboardBlobPrefixes.CreateByFunctionPrefix(functionId);
            return _innerReader.Read(prefix, maximumResults, continuationToken);
        }
    }
}
