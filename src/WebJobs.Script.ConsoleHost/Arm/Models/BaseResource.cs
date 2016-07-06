using Newtonsoft.Json;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public abstract class BaseResource
    {
        [JsonProperty(PropertyName = "subscriptionId")]
        public string SubscriptionId { get; protected set; }

        [JsonProperty(PropertyName = "resourceGroupName")]
        public string ResourceGroupName { get; protected set; }

        public abstract string ArmId { get; }


        public BaseResource(string subscriptionId, string resourceGroupName)
        {
            this.SubscriptionId = subscriptionId;
            this.ResourceGroupName = resourceGroupName;
        }
    }
}