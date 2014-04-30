using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Dashboard.Protocols
{
    internal interface IInvoker
    {
        void TriggerAndOverride(Guid hostId, TriggerAndOverrideMessage message);
    }
}
