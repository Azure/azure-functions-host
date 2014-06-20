using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByJobRunReader
    {
        IResultSegment<RecentInvocationEntry> Read(WebJobRunIdentifier webJobRunId, int maximumResults,
            string continuationToken);
    }
}
