// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using ARMClient.Authentication.Contracts;
using WebJobs.Script.Cli.Arm.Models;

namespace WebJobs.Script.Cli.Arm
{
    internal interface IArmManager
    {
        Task<IEnumerable<Site>> GetFunctionAppsAsync();
        Task<Site> GetFunctionAppAsync(string name);
        Task<Site> CreateFunctionAppAsync(string subscription, string resrouceGroup, string functionAppName, string geoLocation);
        Task<ArmWebsitePublishingCredentials> GetUserAsync();
        Task UpdateUserAsync(string userName, string password);
        Task LoginAsync();
        IEnumerable<string> DumpTokenCache();
        Task SelectTenantAsync(string id);
        void Logout();
        Task<Site> EnsureScmTypeAsync(Site functionApp);
        Task<TenantCacheInfo> GetCurrentTenantAsync();
        IEnumerable<TenantCacheInfo> GetTenants();
        Task<IEnumerable<Subscription>> GetSubscriptionsAsync();
        Task<Site> LoadSitePublishingCredentialsAsync(Site site);
        Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync();
        Task<IEnumerable<ArmWrapper<object>>> getAzureResourceAsync(string resourceName);
        Task<StorageAccount> GetStorageAccountsAsync(ArmWrapper<object> armWrapper);
        Task<Dictionary<string, string>> GetFunctionAppAppSettings(Site functionApp);
    }
}
