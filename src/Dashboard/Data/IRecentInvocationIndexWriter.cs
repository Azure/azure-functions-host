using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexWriter
    {
        void CreateOrUpdate(DateTimeOffset timestamp, Guid id);

        void DeleteIfExists(DateTimeOffset timestamp, Guid id);
    }
}
