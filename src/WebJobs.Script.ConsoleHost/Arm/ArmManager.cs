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

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        private AzureClient _client;

        private HttpContent NullContent { get { return new StringContent(string.Empty); } }

        public ArmManager()
        {

            _client = new AzureClient(retryCount: 3, authHelper: new PersistentAuthHelper
            {
                AzureEnvironments = AzureEnvironments.Prod
            });
        }

        public async Task<IEnumerable<Site>> GetFunctionContainers()
        {
            var subscriptions = await GetSubscriptions();
            var temp = await subscriptions.Select(GetFunctionApps).IgnoreAndFilterFailures();
            return temp.SelectMany(i => i);
        }

        public async Task<ResourceGroup> GetFunctionsResourceGroup(string subscriptionId = null)
        {
            var subscriptions = await GetSubscriptions();
            if (!string.IsNullOrEmpty(subscriptionId))
            {
                subscriptions = subscriptions.Where(s => s.SubscriptionId.Equals(subscriptionId, StringComparison.OrdinalIgnoreCase));
            }

            subscriptions = await subscriptions.Select(Load).IgnoreAndFilterFailures();
            var resourceGroup = subscriptions
                .Select(s => s.ResourceGroups)
                .FirstOrDefault();
            return resourceGroup == null
                ? null
                : await Load(resourceGroup.First());
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
                ScmUrl = resourceGroup.FunctionsSite.ScmHostName,
                BasicAuth = resourceGroup.FunctionsSite.BasicAuth,
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