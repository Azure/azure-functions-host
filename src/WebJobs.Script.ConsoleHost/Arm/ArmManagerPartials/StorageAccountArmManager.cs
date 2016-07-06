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
        public async Task<Dictionary<string, string>> GetStorageAccountKeys(StorageAccount storageAccount)
        {
            return await ArmHttp<Dictionary<string, string>>(HttpMethod.Post, ArmUriTemplates.StorageListKeys.Bind(storageAccount), NullContent);
        }

        public async Task<StorageAccount> CreateFunctionsStorageAccount(ResourceGroup resourceGroup)
        {
            var storageAccountName = $"{Constants.FunctionsStorageAccountNamePrefix}{Guid.NewGuid().ToString().Split('-').First()}".ToLowerInvariant();
            var storageAccount = new StorageAccount(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, storageAccountName);

            await _client.HttpInvoke(HttpMethod.Post, ArmUriTemplates.StorageRegister.Bind(resourceGroup));
            var storageResponse = await _client.HttpInvoke(HttpMethod.Put, ArmUriTemplates.StorageAccount.Bind(storageAccount), new { location = resourceGroup.Location, properties = new { accountType = "Standard_GRS" } });
            await storageResponse.EnsureSuccessStatusCodeWithFullError();

            var isSucceeded = false;
            var tries = 10;
            do
            {
                storageResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.StorageAccount.Bind(storageAccount));
                await storageResponse.EnsureSuccessStatusCodeWithFullError();
                var armStorageAccount = await storageResponse.Content.ReadAsAsync<ArmWrapper<ArmStorage>>();
                isSucceeded = armStorageAccount.properties.provisioningState.Equals("Succeeded", StringComparison.OrdinalIgnoreCase) ||
                    armStorageAccount.properties.provisioningState.Equals("ResolvingDNS", StringComparison.OrdinalIgnoreCase);
                tries--;
                if (!isSucceeded) await Task.Delay(200);
            } while (!isSucceeded && tries > 0);
            return storageAccount;
        }

        public async Task<IEnumerable<StorageAccount>> GetStorageAccounts(ResourceGroup resourceGroup)
        {
            var resources = await GetResourceGroupResources(resourceGroup);
            return resources.value
                .Where(r => r.type.Equals(Constants.StorageAccountArmType, StringComparison.OrdinalIgnoreCase))
                .Select(r => new StorageAccount(resourceGroup.SubscriptionId, resourceGroup.ResourceGroupName, r.name));
        }

        public async Task<StorageAccount> EnsureAStorageAccount(ResourceGroup resourceGroup)
        {
            var storageAccounts = await GetStorageAccounts(resourceGroup);
            return storageAccounts.FirstOrDefault(s => s.StorageAccountName.StartsWith(Constants.FunctionsStorageAccountNamePrefix))
                ?? await CreateFunctionsStorageAccount(resourceGroup);
        }
    }
}