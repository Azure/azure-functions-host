using System;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByJobRunReader : IRecentInvocationIndexByJobRunReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexByJobRunReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client))
        {
        }

        private RecentInvocationIndexByJobRunReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(WebJobRunIdentifier webJobRunId, int maximumResults,
            string continuationToken)
        {
            string prefix = DashboardBlobPrefixes.CreateByJobRunPrefix(webJobRunId);
            return _innerReader.Read(prefix, maximumResults, continuationToken);
        }
    }
}
