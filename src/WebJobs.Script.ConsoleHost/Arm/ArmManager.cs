using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Threading.Tasks;
using System.Net.Http;
using ARMClient.Library;
using WebJobs.Script.ConsoleHost.Arm.Models;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using ARMClient.Authentication;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        private readonly AzureClient _client;
        private readonly IAuthHelper _authHelper;

        private HttpContent NullContent { get { return new StringContent(string.Empty); } }

        public ArmManager()
        {
            _authHelper = new PersistentAuthHelper
            {
                AzureEnvironments = AzureEnvironments.Prod
            };
            _client = new AzureClient(retryCount: 3, authHelper: _authHelper);
        }

        public async Task<IEnumerable<Site>> GetFunctionApps()
        {
            var subscriptions = await GetSubscriptions();
            var temp = await subscriptions.Select(GetFunctionApps).IgnoreAndFilterFailures();
            return temp.SelectMany(i => i);
        }

        public async Task<Site> GetFunctionApp(string name)
        {
            var functionApps = await GetFunctionApps();
            return await Load(functionApps.FirstOrDefault(s => s.SiteName.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        public async Task<Site> CreateFunctionApp(Subscription subscription, string functionAppName, GeoLocation geoLocation)
        {
            var resourceGroup = await EnsureResourceGroup(
                new ResourceGroup(
                    subscription.SubscriptionId,
                    $"AzureFunctions-{geoLocation.ToString()}",
                    geoLocation.ToString())
                );

            var storageAccount = await EnsureAStorageAccount(resourceGroup);
            var functionApp = new Site(subscription.SubscriptionId, resourceGroup.ResourceGroupName, functionAppName);
            var keys = await GetStorageAccountKeys(storageAccount);
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccount.StorageAccountName};AccountKey={keys.First().Value}";
            var armFunctionApp = await ArmHttp<ArmWrapper<object>>(HttpMethod.Put, ArmUriTemplates.Site.Bind(functionApp),
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

        public Task Login()
        {
            _authHelper.ClearTokenCache();
            return _authHelper.AcquireTokens();
        }

        public IEnumerable<string> DumpTokenCache()
        {
            return _authHelper.DumpTokenCache();
        }

        public Task SelectTenant(string id)
        {
            return _authHelper.GetToken(id);
        }

        public void Logout()
        {
            _authHelper.ClearTokenCache();
        }

        public async Task<Site> EnsureScmType(Site functionApp)
        {
            functionApp = await LoadSiteConfig(functionApp);

            if (string.IsNullOrEmpty(functionApp.ScmType) ||
                functionApp.ScmType.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateSiteConfig(functionApp, new { properties = new { scmType = "LocalGit" } });
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            return functionApp;
        }

        private async Task<T> ArmHttp<T>(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            await response.EnsureSuccessStatusCodeWithFullError();

            return await response.Content.ReadAsAsync<T>();
        }

        public async Task<string> GetCurrentTenantDomain()
        {
            var token = await _authHelper.GetToken(id: string.Empty);
            return _authHelper.GetTenantInfo(token.TenantId).domain;
        }

        private async Task ArmHttp(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            await response.EnsureSuccessStatusCodeWithFullError();
        }
    }
}