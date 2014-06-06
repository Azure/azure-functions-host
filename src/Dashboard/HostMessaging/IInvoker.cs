using System;
using System.Collections.Generic;
using Dashboard.Data;

namespace Dashboard.HostMessaging
{
    public interface IInvoker
    {
        Guid TriggerAndOverride(string queueName, FunctionSnapshot function, IDictionary<string, string> arguments,
            Guid? parentId, string reason);
    }
}
