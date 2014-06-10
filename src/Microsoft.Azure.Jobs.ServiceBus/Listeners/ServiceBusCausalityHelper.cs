using System;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal static class ServiceBusCausalityHelper
    {
        private const string _parentGuidFieldName = "$AzureJobsParentId";

        public static void EncodePayload(Guid functionOwner, BrokeredMessage msg)
        {
            msg.Properties[_parentGuidFieldName] = functionOwner.ToString();
        }

        public static Guid? GetOwner(BrokeredMessage msg)
        {
            object parent;
            if (msg.Properties.TryGetValue(_parentGuidFieldName, out parent))
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
            return null;
        }
    }
}
