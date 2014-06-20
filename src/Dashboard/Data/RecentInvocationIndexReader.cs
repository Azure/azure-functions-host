using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexReader : IRecentInvocationIndexReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client))
        {
        }

        private RecentInvocationIndexReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(int maximumResults, string continuationToken)
        {
            return _innerReader.Read(DashboardBlobPrefixes.Flat, maximumResults, continuationToken);
        }
    }
}
