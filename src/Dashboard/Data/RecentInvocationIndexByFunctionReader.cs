using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByFunctionReader : IRecentInvocationIndexByFunctionReader
    {
        private readonly CloudBlobContainer _container;

        [CLSCompliant(false)]
        public RecentInvocationIndexByFunctionReader(CloudBlobClient client)
            : this (client.GetContainerReference(DashboardContainerNames.RecentFunctionsContainerName))
        {
        }

        [CLSCompliant(false)]
        public RecentInvocationIndexByFunctionReader(CloudBlobContainer container)
        {
            _container = container;
        }

        public IResultSegment<RecentInvocationEntry> Read(string functionId, int maximumResults, string continuationToken)
        {
            string prefix = DashboardBlobPrefixes.CreateByFunctionPrefix(functionId);
            return RecentInvocationIndexReader.Read(_container, prefix, maximumResults, continuationToken);
        }
    }
}
