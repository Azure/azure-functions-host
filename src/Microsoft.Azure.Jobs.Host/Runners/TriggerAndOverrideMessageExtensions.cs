using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal static class TriggerAndOverrideMessageExtensions
    {
        public static InvokeTriggerReason GetTriggerReason(this TriggerAndOverrideMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            return InvokeTriggerReason.Create(message.Id, message.Reason, message.ParentId);
        }
    }
}
