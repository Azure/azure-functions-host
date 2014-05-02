using System;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal class ServiceBusCausalityHelper
    {
        const string parentGuidFieldName = "$AzureJobsParentId";

        public void EncodePayload(Guid functionOwner, BrokeredMessage msg)
        {
            msg.Properties[parentGuidFieldName] = functionOwner.ToString();
        }

        public Guid GetOwner(BrokeredMessage msg)
        {
            object parent;
            if (msg.Properties.TryGetValue(parentGuidFieldName, out parent))
            {
                var parentString = parent as string;
                if (parentString != null)
                {
                    Guid parentGuid;
                    if (Guid.TryParse(parentString, out parentGuid))
                    {
                        return parentGuid;
                    }
                }
            }
            return Guid.Empty;
        }
    }
}