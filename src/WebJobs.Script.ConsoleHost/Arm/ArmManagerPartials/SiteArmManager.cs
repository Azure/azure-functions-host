// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
                LoadSiteObject(site),
                LoadSitePublishingCredentials(site),
                LoadSiteConfig(site)
            }
            //.IgnoreFailures()
            .WhenAll();
            return site;
        }

        public async Task<Site> LoadAppSettings(Site site)
        {
            var armAppSettings = await ArmHttp<ArmWrapper<Dictionary<string, string>>>(HttpMethod.Post, ArmUriTemplates.ListSiteAppSettings.Bind(site), NullContent);

            site.AppSettings = armAppSettings.properties;
            return site;
        }

        public async Task<Site> LoadSiteObject(Site site)
        {
            var armSite = await ArmHttp<ArmWrapper<ArmWebsite>>(HttpMethod.Get, ArmUriTemplates.Site.Bind(site));

            site.HostName = armSite.properties.enabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) == -1);
            //site.ScmUri = armSite.properties.enabledHostNames.FirstOrDefault(s => s.IndexOf(".scm.", StringComparison.OrdinalIgnoreCase) != -1);
            site.Location = armSite.location;
            return site;
        }

        public async Task<Site> LoadSitePublishingCredentials(Site site)
        {
            return site
                .MergeWith(
                    await ArmHttp<ArmWrapper<ArmWebsitePublishingCredentials>>(HttpMethod.Post, ArmUriTemplates.SitePublishingCredentials.Bind(site)),
                    t => t.properties
                );
        }

        public async Task<Site> LoadSiteConfig(Site site)
        {
            return site.MergeWith(
                    await ArmHttp<ArmWrapper<ArmWebsiteConfig>>(HttpMethod.Get, ArmUriTemplates.SiteConfig.Bind(site)),
                    t => t.properties
                );
        }

        public async Task<Site> UpdateSiteConfig(Site site, object config)
        {
            return site.MergeWith(
                    await ArmHttp<ArmWrapper<ArmWebsiteConfig>>(HttpMethod.Put, ArmUriTemplates.SiteConfig.Bind(site), config),
                    t => t.properties
                );
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
            await ArmHttp(HttpMethod.Put, ArmUriTemplates.PutSiteAppSettings.Bind(site), new { properties = site.AppSettings });
            return site;
        }

        public async Task<Site> UpdateConfig(Site site, object config)
        {
            await ArmHttp(HttpMethod.Put, ArmUriTemplates.SiteConfig.Bind(site), config);
            return site;
        }
    }
}