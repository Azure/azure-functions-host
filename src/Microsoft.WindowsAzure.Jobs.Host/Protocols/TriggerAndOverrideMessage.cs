using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    [JsonTypeName("TriggerAndOverride")]
    internal class TriggerAndOverrideMessage : HostMessage
    {
        public Guid Id { get; set; }

        public string FunctionId { get; set; }

        public IDictionary<string, string> Arguments { get; set; }

        public string Reason { get; set; }

        public Guid? ParentId { get; set; }

        public InvokeTriggerReason GetTriggerReason()
        {
            return InvokeTriggerReason.Create(Id, Reason, ParentId);
        }
    }
}
