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