// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using System.Collections.Generic;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class Subscription
    {
        [JsonProperty(PropertyName = "subscriptionId")]
        public string SubscriptionId { get; private set; }

        public IEnumerable<ResourceGroup> ResourceGroups { get; set; }

        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; private set; }

        public Subscription(string subscriptionId, string displayName)
        {
            this.SubscriptionId = subscriptionId;
            this.DisplayName = displayName;
        }
    }
}