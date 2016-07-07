// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace WebJobs.Script.Cli.Arm.Models
{
    internal class ArmSubscription
    {
        [JsonProperty(PropertyName = "subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonProperty(PropertyName = "displayName")]
        public string DisplayName { get; set; }
    }
}