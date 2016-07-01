using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using WebJobs.Script.ConsoleHost.Arm.Models;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        public async Task<ResourceGroup> Load(ResourceGroup resourceGroup)
        {
            var armResourceGroupResourcesResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.ResourceGroupResources.Bind(resourceGroup));
            await armResourceGroupResourcesResponse.EnsureSuccessStatusCodeWithFullError();
            var resources = await armResourceGroupResourcesResponse.Content.ReadAsAsync<ArmArrayWrapper<object>>();

            resourceGroup.FunctionsApps = resources.value
                .Where(r => r.type.Equals(Constants.WebAppArmType, StringComparison.OrdinalIgnoreCase) &&
                            r.kind?.Equals(Constants.FunctionAppArmKind, StringComparison.OrdinalIgnoreCase) == true)
                .Select(r => new Site(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, r.name));

            resourceGroup.StorageAccounts = resources.value
                .Where(r => r.type.Equals(Constants.StorageAccountArmType, StringComparison.OrdinalIgnoreCase))
                .Select(r => new StorageAccount(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, r.name));

            return resourceGroup;
        }

        public async Task<ResourceGroup> CreateResourceGroup(string subscriptionId, string location)
        {
            var resourceGroup = new ResourceGroup(subscriptionId, Constants.FunctionsResourceGroupName, location);
            var resourceGroupResponse = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.ResourceGroup.Bind(resourceGroup), new { properties = new { }, location = location });
            await resourceGroupResponse.EnsureSuccessStatusCodeWithFullError();

            return resourceGroup;
        }
    }
}