using System;

namespace Dashboard.Data
{
    public interface IRecentFunctionWriter
    {
        void CreateOrUpdate(DateTimeOffset timestamp, Guid id);

        void DeleteIfExists(DateTimeOffset timestamp, Guid id);
    }
}
