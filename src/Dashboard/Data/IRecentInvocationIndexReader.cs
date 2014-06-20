namespace Dashboard.Data
{
    public interface IRecentInvocationIndexReader
    {
        IResultSegment<RecentInvocationEntry> Read(int maximumResults, string continuationToken);
    }
}
