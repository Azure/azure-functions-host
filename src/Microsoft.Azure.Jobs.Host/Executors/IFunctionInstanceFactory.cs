using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IFunctionInstanceFactory
    {
        IFunctionInstance Create(Guid id, Guid? parentId, ExecutionReason reason,
            IDictionary<string, object> parameters);
    }
}
