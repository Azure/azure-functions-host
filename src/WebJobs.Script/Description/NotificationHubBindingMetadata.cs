// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.NotificationHubs;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class NotificationHubBindingMetadata : BindingMetadata
    {
        public string TagExpression { get; set; }

        [AllowNameResolution]
        public string ConnectionString { get; set; }

        [AllowNameResolution]
        public string HubName { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            NotificationHubsConfiguration config = new NotificationHubsConfiguration();
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                config.ConnectionString = ConnectionString;
            }
            if (!string.IsNullOrEmpty(HubName))
            {
                config.HubName = HubName;
            }
            configBuilder.Config.UseNotificationHubs(config);
        }
    }
}
