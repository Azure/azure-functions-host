using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByParentWriter
    {
        void CreateOrUpdate(Guid parentId, DateTimeOffset timestamp, Guid id);

        void DeleteIfExists(Guid parentId, DateTimeOffset timestamp, Guid id);
    }
}
