// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Listeners
{
    internal static class ServiceBusCausalityHelper
    {
        private const string _parentGuidFieldName = "$AzureWebJobsParentId";

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
