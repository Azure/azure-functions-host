// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm.Models;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli.Arm
{
    internal partial class ArmManager
    {
        public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync()
        {
            var subscriptionsResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.Subscriptions.Bind(string.Empty));
            subscriptionsResponse.EnsureSuccessStatusCode();

            var subscriptions = await subscriptionsResponse.Content.ReadAsAsync<ArmSubscriptionsArray>();
            return subscriptions.Value.Select(s => new Subscription(s.SubscriptionId, s.DisplayName));
        }

        public async Task<IEnumerable<Site>> GetFunctionAppsAsync(Subscription subscription)
        {
            var armSubscriptionWebAppsResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionWebApps.Bind(subscription));
            armSubscriptionWebAppsResponse.EnsureSuccessStatusCode();

            var armSubscriptionWebApps = await armSubscriptionWebAppsResponse.Content.ReadAsAsync<ArmArrayWrapper<ArmWebsite>>();
            Func<string, string> getResourceGroupName = id => id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[3];

            return armSubscriptionWebApps.Value
                .Where(s => s.Kind?.IndexOf(Constants.FunctionAppArmKind, StringComparison.OrdinalIgnoreCase) != -1)
                .Select(s => new Site(subscription.SubscriptionId, getResourceGroupName(s.Id), s.Name) { Location = s.Location });
        }

        public async Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync(Subscription subscription)
        {
            var armSubscriptionStorageResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionStorageAccounts.Bind(subscription));
            armSubscriptionStorageResponse.EnsureSuccessStatusCode();

            var armSubscriptionStorageAccounts = await armSubscriptionStorageResponse.Content.ReadAsAsync<ArmArrayWrapper<ArmStorage>>();
            Func<string, string> getResourceGroupName = id => id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)[3];

            return armSubscriptionStorageAccounts.Value
                .Select(s => new StorageAccount(subscription.SubscriptionId, getResourceGroupName(s.Id), s.Name, s.Location));
        }

        public async Task<ArmWebsitePublishingCredentials> GetUserAsync()
        {
            var response = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.PublishingUsers.Bind(string.Empty));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsAsync<ArmWrapper<ArmWebsitePublishingCredentials>>()).Properties;
        }

        public async Task UpdateUserAsync(string userName, string password)
        {
            await ArmHttpAsync(HttpMethod.Put, ArmUriTemplates.PublishingUsers.Bind(string.Empty), new { properties = new ArmWebsitePublishingCredentials { PublishingUserName = userName, PublishingPassword = password } });
        }
    }
}