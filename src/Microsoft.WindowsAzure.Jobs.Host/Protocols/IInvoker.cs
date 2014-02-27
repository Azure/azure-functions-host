using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal interface IInvoker
    {
        void TriggerAndOverride(Guid hostId, TriggerAndOverrideMessage message);
    }
}
