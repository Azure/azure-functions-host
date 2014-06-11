using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    public interface IFunctionQueuedLogger
    {
        void LogFunctionQueued(Guid id, IDictionary<string, string> arguments, Guid? parentId, DateTimeOffset queueTime,
            string functionId, string functionFullName, string functionShortName);
    }
}
