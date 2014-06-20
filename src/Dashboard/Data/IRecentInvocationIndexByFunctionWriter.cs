using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByFunctionWriter
    {
        void CreateOrUpdate(string functionId, DateTimeOffset timestamp, Guid id);

        void DeleteIfExists(string functionId, DateTimeOffset timestamp, Guid id);
    }
}
