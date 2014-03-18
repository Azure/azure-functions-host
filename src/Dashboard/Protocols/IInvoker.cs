using System;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Dashboard.Protocols
{
    internal interface IInvoker
    {
        void TriggerAndOverride(Guid hostId, TriggerAndOverrideMessage message);
    }
}
