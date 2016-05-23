// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.NotificationHubs;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class NotificationHubBindingMetadata : BindingMetadata
    {
        public string TagExpression { get; set; }

        public NotificationPlatform Platform { get; set; }

        public string HubName { get; set; }
    }
}
