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
        public async Task<Dictionary<string, string>> GetStorageAccountKeysAsync(StorageAccount storageAccount)
        {
            return await ArmHttpAsync<Dictionary<string, string>>(HttpMethod.Post, ArmUriTemplates.StorageListKeys.Bind(storageAccount));
        }

        public async Task<StorageAccount> CreateFunctionsStorageAccountAsync(ResourceGroup resourceGroup)
        {
            var storageAccountName = $"{Constants.FunctionsStorageAccountNamePrefix}{Guid.NewGuid().ToString().Split('-').First()}".ToLowerInvariant();
            var storageAccount = new StorageAccount(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, storageAccountName, resourceGroup.Location);

            await _client.HttpInvoke(HttpMethod.Post, ArmUriTemplates.StorageRegister.Bind(resourceGroup));
            var storageResponse = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.StorageAccount.Bind(storageAccount), new { location = resourceGroup.Location, properties = new { accountType = "Standard_GRS" } });
            storageResponse.EnsureSuccessStatusCode();

            var isSucceeded = false;
            var tries = 10;
            do
            {
                storageResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.StorageAccount.Bind(storageAccount));
                storageResponse.EnsureSuccessStatusCode();
                var armStorageAccount = await storageResponse.Content.ReadAsAsync<ArmWrapper<ArmStorage>>();
                isSucceeded = armStorageAccount.Properties.provisioningState.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) ||
                    armStorageAccount.Properties.provisioningState.Equals("ResolvingDNS", StringComparison.OrdinalIgnoreCase);
                tries--;
                if (!isSucceeded) await Task.Delay(200);
            } while (!isSucceeded && tries > 0);
            return storageAccount;
        }

        public async Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync(ResourceGroup resourceGroup)
        {
            var resources = await GetResourceGroupResourcesAsync(resourceGroup);
            return resources.Value
                .Where(r => r.Type.Equals(Constants.StorageAccountArmType, StringComparison.OrdinalIgnoreCase))
                .Select(r => new StorageAccount(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, r.Name, r.Location));
        }

        public async Task<StorageAccount> EnsureAStorageAccountAsync(ResourceGroup resourceGroup)
        {
            var storageAccounts = await GetStorageAccountsAsync(resourceGroup);
            return storageAccounts.FirstOrDefault(s => s.StorageAccountName.StartsWith(Constants.FunctionsStorageAccountNamePrefix))
                ?? await CreateFunctionsStorageAccountAsync(resourceGroup);
        }

        public async Task<StorageAccount> LoadAsync(StorageAccount storageAccount)
        {
            var storageResponse = await _client.HttpInvoke(HttpMethod.Post, ArmUriTemplates.StorageListKeys.Bind(storageAccount));
            storageResponse.EnsureSuccessStatusCode();

            var keys = await storageResponse.Content.ReadAsAsync<Dictionary<string, string>>();
            storageAccount.StorageAccountKey = keys.Select(s => s.Value).FirstOrDefault();
            return storageAccount;
        }
    }
}