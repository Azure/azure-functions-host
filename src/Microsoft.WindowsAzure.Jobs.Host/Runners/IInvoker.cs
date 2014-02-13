using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal interface IInvoker
    {
        void Invoke(Guid hostId, InvocationMessage message);
    }
}
