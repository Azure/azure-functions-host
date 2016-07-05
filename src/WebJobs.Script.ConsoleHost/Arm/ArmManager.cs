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
            if (!functionApp.IsLoaded)
            {
                functionApp = await LoadSiteConfig(functionApp);
            }

            if (string.IsNullOrEmpty(functionApp.ScmType) ||
                functionApp.ScmType.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateSiteConfig(functionApp, new { scmType = "LocalGit" });
            }

            return functionApp;
        }

        private async Task<T> ArmHttp<T>(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            await response.EnsureSuccessStatusCodeWithFullError();

            return await response.Content.ReadAsAsync<T>();
        }

        private async Task ArmHttp(HttpMethod method, Uri uri, object payload = null)
        {
            var response = await _client.HttpInvoke(method, uri, payload);
            await response.EnsureSuccessStatusCodeWithFullError();
        }

        public async Task<FunctionsContainer> CreateFunctionContainer(string subscriptionId, string location, string serverFarmId = null)
        {
            var resourceGroup = await GetFunctionsResourceGroup(subscriptionId) ?? await CreateResourceGroup(subscriptionId, location);
            return await CreateFunctionContainer(resourceGroup, serverFarmId);
        }

        public async Task<FunctionsContainer> CreateFunctionContainer(ResourceGroup resourceGroup, string serverFarmId = null)
        {
            if (resourceGroup.FunctionsStorageAccount == null)
            {
                resourceGroup.FunctionsStorageAccount = await CreateFunctionsStorageAccount(resourceGroup);
            }
            else
            {
                await Load(resourceGroup.FunctionsStorageAccount);
            }

            if (resourceGroup.FunctionsSite == null)
            {
                resourceGroup.FunctionsSite = await CreateFunctionsSite(resourceGroup, serverFarmId);
            }
            else
            {
                await Load(resourceGroup.FunctionsSite);
            }

            await UpdateSiteAppSettings(resourceGroup.FunctionsSite, resourceGroup.FunctionsStorageAccount);

            return new FunctionsContainer
            {
                ScmUrl = resourceGroup.FunctionsSite.ScmUri,
                ArmId = resourceGroup.FunctionsSite.ArmId
            };
        }

        public async Task UpdateSiteAppSettings(Site site, StorageAccount storageAccount)
        {
            // Assumes site and storage are loaded
            var update = false;
            if (!site.AppSettings.ContainsKey(Constants.AzureStorageAppSettingsName))
            {
                site.AppSettings[Constants.AzureStorageAppSettingsName] = string.Format(Constants.StorageConnectionStringTemplate, storageAccount.StorageAccountName, storageAccount.StorageAccountKey);
                site.AppSettings[Constants.AzureStorageDashboardAppSettingsName] = string.Format(Constants.StorageConnectionStringTemplate, storageAccount.StorageAccountName, storageAccount.StorageAccountKey);
                update = true;
            }

            if (!site.AppSettings.ContainsKey(Constants.FunctionsExtensionVersion))
            {
                site.AppSettings[Constants.FunctionsExtensionVersion] = Constants.Latest;
                update = true;
            }

            if (update)
                await UpdateSiteAppSettings(site);
        }
    }
}