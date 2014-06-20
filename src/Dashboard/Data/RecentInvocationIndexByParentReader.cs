using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByParentReader : IRecentInvocationIndexByParentReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexByParentReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client))
        {
        }

        private RecentInvocationIndexByParentReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(Guid parentId, int maximumResults, string continuationToken)
        {
            string prefix = DashboardBlobPrefixes.CreateByParentPrefix(parentId);
            return _innerReader.Read(prefix, maximumResults, continuationToken);
        }
    }
}
