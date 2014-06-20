namespace Dashboard.Data
{
    interface IBlobRecentInvocationIndexReader
    {
        IResultSegment<RecentInvocationEntry> Read(string prefix, int maximumResults, string continuationToken);
    }
}
