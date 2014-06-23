namespace Dashboard.Data
{
    interface IBlobRecentInvocationIndexReader
    {
        IResultSegment<RecentInvocationEntry> Read(string relativePrefix, int maximumResults, string continuationToken);
    }
}
