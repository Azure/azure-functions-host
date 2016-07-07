// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ARMClient.Authentication;
using ARMClient.Authentication.Contracts;
using ARMClient.Library;
using WebJobs.Script.Cli.Arm.Models;
using WebJobs.Script.Cli.Extensions;

namespace WebJobs.Script.Cli.Arm
{
    internal partial class ArmManager : IArmManager
    {
        private readonly IAzureClient _client;
        private readonly IAuthHelper _authHelper;

        public ArmManager(IAuthHelper authHelper, IAzureClient client)
        {
            _authHelper = authHelper;
            _client = client;
        }

        public async Task<IEnumerable<Site>> GetFunctionAppsAsync()
        {
            var subscriptions = await GetSubscriptionsAsync();
            var temp = await subscriptions.Select(GetFunctionAppsAsync).IgnoreAndFilterFailures();
            return temp.SelectMany(i => i);
        }

        public async Task<Site> GetFunctionAppAsync(string name)
        {
            var functionApps = await GetFunctionAppsAsync();
            return await LoadAsync(functionApps.FirstOrDefault(s => s.SiteName.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<Site> CreateFunctionAppAsync(Subscription subscription, string functionAppName, string geoLocation)
        {
            var resourceGroup = await EnsureResourceGroupAsync(
                new ResourceGroup(
                    subscription.SubscriptionId,
                    $"AzureFunctions-{geoLocation.ToString()}",
                    geoLocation.ToString())
                );

            var storageAccount = await EnsureAStorageAccountAsync(resourceGroup);
            var functionApp = new Site(subscription.SubscriptionId, resourceGroup.ResourceGroupName, functionAppName);
            var keys = await GetStorageAccountKeysAsync(storageAccount);
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccount.StorageAccountName};AccountKey={keys.First().Value}";
            var armFunctionApp = await ArmHttpAsync<ArmWrapper<object>>(HttpMethod.Put, ArmUriTemplates.Site.Bind(functionApp),
                    new
                    {
                        properties = new
                        {
                            siteConfig = new
                            {
                                appSettings = new Dictionary<string, string> {
                                    { "AzureWebJobsStorage", connectionString },
                                    { "AzureWebJobsDashboard", connectionString },
                                    { "FUNCTIONS_EXTENSION_VERSION", "latest" },
                                    { "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", connectionString },
                                    { "WEBSITE_CONTENTSHARE", storageAccount.StorageAccountName.ToLowerInvariant() },
                                    { $"{storageAccount.StorageAccountName}_STORAGE", connectionString },
                                    { "AZUREJOBS_EXTENSION_VERSION", "beta" },
                                    { "WEBSITE_NODE_DEFAULT_VERSION", "4.1.2" }
                                }
                                .Select(e => new { name = e.Key, value = e.Value})
                            },
                            sku = "Dynamic"
                        },
                        location = geoLocation.ToString(),
                        kind = "functionapp"
                    });

            return functionApp;
        }

        public Task LoginAsync()
        {
            _authHelper.ClearTokenCache();
            return _authHelper.AcquireTokens();
        }

        public IEnumerable<string> DumpTokenCache()
        {
            return _authHelper.DumpTokenCache();
        }

        public Task SelectTenantAsync(string id)
        {
            return _authHelper.GetToken(id);
        }

        public void Logout()
        {
            _authHelper.ClearTokenCache();
        }

        public async Task<Site> EnsureScmTypeAsync(Site functionApp)
        {
            functionApp = await LoadSiteConfigAsync(functionApp);

            if (string.IsNullOrEmpty(functionApp.ScmType) ||
                functionApp.ScmType.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateSiteConfigAsync(functionApp, new { properties = new { scmType = "LocalGit" } });
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            return functionApp;
        }

        private async Task<T> ArmHttpAsync<T>(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsAsync<T>();
        }

        public async Task<TenantCacheInfo> GetCurrentTenantAsync()
        {
            var token = await _authHelper.GetToken(id: string.Empty);
            return _authHelper.GetTenantInfo(token.TenantId);
        }

        private async Task ArmHttpAsync(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync()
        {
            var subscriptions = await GetSubscriptionsAsync();
            var temp = await subscriptions.Select(GetStorageAccountsAsync).IgnoreAndFilterFailures();
            return temp.SelectMany(i => i);
        }

        public async Task<IEnumerable<ArmWrapper<object>>> getAzureResourceAsync(string resourceName)
        {
            var subscriptions = await GetSubscriptionsAsync();
            var temp = await subscriptions.Select(s => GetResourcesByNameAsync(s, resourceName)).IgnoreAndFilterFailures();
            return temp.SelectMany(i => i);
        }

        private async Task<IEnumerable<ArmWrapper<object>>> GetResourcesByNameAsync(Subscription subscription, string resourceName)
        {
            var armSubscriptionResourcesResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionResourceByName.Bind(new { subscriptionId = subscription.SubscriptionId, resourceName = resourceName }));
            armSubscriptionResourcesResponse.EnsureSuccessStatusCode();

            var resources = await armSubscriptionResourcesResponse.Content.ReadAsAsync<ArmArrayWrapper<object>>();
            return resources.Value;
        }

        public async Task<StorageAccount> GetStorageAccountsAsync(ArmWrapper<object> armWrapper)
        {
            var regex = new Regex("/subscriptions/(.*)/resourceGroups/(.*)/providers/Microsoft.Storage/storageAccounts/(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = regex.Match(armWrapper.Id);
            if (match.Success)
            {
                var storageAccount = new StorageAccount(match.Groups[1].ToString(), match.Groups[2].ToString(), match.Groups[3].ToString(), string.Empty);
                return await LoadAsync(storageAccount);
            }
            else
            {
                return null;
            }
        }
    }
}