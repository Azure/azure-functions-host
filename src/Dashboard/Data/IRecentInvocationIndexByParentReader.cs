using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByParentReader
    {
        IResultSegment<RecentInvocationEntry> Read(Guid parentId, int maximumResults, string continuationToken);
    }
}
