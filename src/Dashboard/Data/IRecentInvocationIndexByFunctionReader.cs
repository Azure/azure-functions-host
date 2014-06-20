namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByFunctionReader
    {
        IResultSegment<RecentInvocationEntry> Read(string functionId, int maximumResults, string continuationToken);
    }
}
