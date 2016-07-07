// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm.Models;

namespace WebJobs.Script.Cli.Arm
{
    internal partial class ArmManager
    {
        public async Task<ArmArrayWrapper<object>> GetResourceGroupResourcesAsync(ResourceGroup resourceGroup)
        {
            return await ArmHttpAsync<ArmArrayWrapper<object>>(HttpMethod.Get, ArmUriTemplates.ResourceGroupResources.Bind(resourceGroup));
        }

        public async Task<ResourceGroup> CreateResourceGroupAsync(ResourceGroup resourceGroup)
        {
            await ArmHttpAsync(HttpMethod.Put, ArmUriTemplates.ResourceGroup.Bind(resourceGroup), new { properties = new { }, location = resourceGroup.Location });
            return resourceGroup;
        }

        public async Task<ResourceGroup> EnsureResourceGroupAsync(ResourceGroup resourceGroup)
        {
            var rgResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.ResourceGroup.Bind(resourceGroup));

            return rgResponse.IsSuccessStatusCode
                ? resourceGroup
                : await CreateResourceGroupAsync(resourceGroup);
        }
    }
}