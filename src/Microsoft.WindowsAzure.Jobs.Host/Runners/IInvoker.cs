using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IInvoker
    {
        void Invoke(Guid hostId, InvocationMessage message);
    }
}
