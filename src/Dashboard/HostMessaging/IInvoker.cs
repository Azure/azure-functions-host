using System;
using System.Collections.Generic;
using Dashboard.Data;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.HostMessaging
{
    public interface IInvoker
    {
        Guid TriggerAndOverride(string queueName, FunctionSnapshot function, IDictionary<string, string> arguments,
            Guid? parentId, ExecutionReason reason);
    }
}
