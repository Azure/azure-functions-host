using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using WebJobs.Script.ConsoleHost.Arm.Models;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        public async Task<Site> Load(Site site)
        {
            await new[]
            {
                LoadAppSettings(site),
                LoadSiteConfig(site),
                LoadSitePublishingCredentials(site)
            }
            //.IgnoreFailures()
            .WhenAll();
            return site;
        }

        public async Task<Site> LoadAppSettings(Site site)
        {
            var siteResponse = await _client.HttpInvoke(HttpMethod.Post, ArmUriTemplates.ListSiteAppSettings.Bind(site), NullContent);
            await siteResponse.EnsureSuccessStatusCodeWithFullError();

            var armAppSettings = await siteResponse.Content.ReadAsAsync<ArmWrapper<Dictionary<string, string>>>();
            site.AppSettings = armAppSettings.properties;
            return site;
        }

        public async Task<Site> LoadSiteConfig(Site site)
        {
            var siteResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.Site.Bind(site));
            await siteResponse.EnsureSuccessStatusCodeWithFullError();

            var armSite = await siteResponse.Content.ReadAsAsync<ArmWrapper<ArmWebsite>>();
            site.HostName = $"https://{armSite.properties.enabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) == -1)}";
            site.HostName = $"https://{armSite.properties.enabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) != -1)}";
            return site;
        }

        public async Task<Site> LoadSitePublishingCredentials(Site site)
        {
            var siteResponse = await _client.HttpInvoke(HttpMethod.Post, ArmUriTemplates.SitePublishingCredentials.Bind(site), NullContent);
            await siteResponse.EnsureSuccessStatusCodeWithFullError();

            var creds = await siteResponse.Content.ReadAsAsync<ArmWrapper<ArmWebsitePublishingCredentials>>();
            site.BasicAuth = $"{creds.properties.publishingUserName}:{creds.properties.publishingPassword}".ToBase64();
            return site;
        }

        public async Task<Site> CreateFunctionsSite(ResourceGroup resourceGroup, string serverFarmId)
        {
            if (resourceGroup.FunctionsStorageAccount == null)
            {
                throw new InvalidOperationException("storage account can't be null");
            }

            var storageAccount = resourceGroup.FunctionsStorageAccount;
            await _client.HttpInvoke(HttpMethod.Post, ArmUriTemplates.WebsitesRegister.Bind(resourceGroup), NullContent);

            var siteName = $"{Constants.FunctionsSitePrefix}{Guid.NewGuid().ToString().Split('-').First()}";
            var site = new Site(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, siteName);
            var siteObject = new
            {
                properties = new
                {
                    serverFarmId = serverFarmId,
                    siteConfig = new
                    {
                        appSettings = new[]
                        {
                                new { name = Constants.AzureStorageAppSettingsName, value = storageAccount.GetConnectionString() },
                                new { name = Constants.AzureStorageDashboardAppSettingsName, value = storageAccount.GetConnectionString() },
                                new { name = Constants.FunctionsExtensionVersion, value = Constants.Latest }
                            },
                        scmType = "LocalGit"
                    }
                },
                location = resourceGroup.Location,
                kind = Constants.FunctionAppArmKind
            };
            var siteResponse = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.Site.Bind(site), siteObject);
            var armSite = await siteResponse.EnsureSuccessStatusCodeWithFullError();

            await Load(site);
            return site;
        }

        public async Task<Site> UpdateSiteAppSettings(Site site)
        {
            var armResponse = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.PutSiteAppSettings.Bind(site), new { properties = site.AppSettings });
            await armResponse.EnsureSuccessStatusCodeWithFullError();
            return site;
        }

        public async Task<Site> UpdateConfig(Site site, object config)
        {
            var response = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.SiteConfig.Bind(site), config);
            await response.EnsureSuccessStatusCodeWithFullError();
            return site;
        }
    }
}