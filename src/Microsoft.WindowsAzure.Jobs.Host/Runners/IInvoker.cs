using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal interface IInvoker
    {
        void TriggerAndOverride(Guid hostId, TriggerAndOverrideMessage message);
    }
}
