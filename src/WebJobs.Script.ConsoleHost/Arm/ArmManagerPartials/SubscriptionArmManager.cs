using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using WebJobs.Script.ConsoleHost.Arm.Models;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        public async Task<IEnumerable<Subscription>> GetSubscriptions()
        {
            var subscriptionsResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.Subscriptions.Bind(string.Empty));
            await subscriptionsResponse.EnsureSuccessStatusCodeWithFullError();

            var subscriptions = await subscriptionsResponse.Content.ReadAsAsync<ArmSubscriptionsArray>();
            return subscriptions.value.Select(s => new Subscription(s.subscriptionId, s.displayName));
        }

        public async Task<Subscription> Load(Subscription subscription)
        {
                var armResourceGroupsResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.ResourceGroups.Bind(subscription));
                await armResourceGroupsResponse.EnsureSuccessStatusCodeWithFullError();

                var armResourceGroups = await armResourceGroupsResponse.Content.ReadAsAsync<ArmArrayWrapper<ArmResourceGroup>>();

                subscription.ResourceGroups = armResourceGroups.value
                    .Select(rg => new ResourceGroup(subscription.SubscriptionId, rg.name, rg.location) { Tags = rg.tags });

                return subscription;
        }

        public async Task<IEnumerable<Site>> GetFunctionApps(Subscription subscription)
        {
            var armSubscriptionWebAppsResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionWebApps.Bind(subscription));
            await armSubscriptionWebAppsResponse.EnsureSuccessStatusCodeWithFullError();

            var armSubscriptionWebApps = await armSubscriptionWebAppsResponse.Content.ReadAsAsync<ArmArrayWrapper<ArmWebsite>>();
            Func<string, string> getResourceGroupName = id => id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[3];

            return armSubscriptionWebApps.value
                .Where(s => s.kind?.Equals(Constants.FunctionAppArmKind, StringComparison.OrdinalIgnoreCase) == true)
                .Select(s => new Site(subscription.SubscriptionId, getResourceGroupName(s.id), s.name) { Location = s.location });
        }

        public async Task<ArmPublishingUser> GetUser()
        {
            var response = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.PublishingUsers.Bind(string.Empty));
            await response.EnsureSuccessStatusCodeWithFullError();
            return (await response.Content.ReadAsAsync<ArmWrapper<ArmPublishingUser>>()).properties;
        }
    }
}