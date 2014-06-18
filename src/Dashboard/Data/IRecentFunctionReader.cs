namespace Dashboard.Data
{
    public interface IRecentFunctionReader
    {
        IResultSegment<RecentFunctionInstance> Read(int maximumResults, string continuationToken);
    }
}
