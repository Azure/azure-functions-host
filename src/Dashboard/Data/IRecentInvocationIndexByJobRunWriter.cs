using System;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByJobRunWriter
    {
        void CreateOrUpdate(WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp, Guid id);

        void DeleteIfExists(WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp, Guid id);
    }
}
