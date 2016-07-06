// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using WebJobs.Script.ConsoleHost.Arm.Models;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        public async Task<ArmArrayWrapper<object>> GetResourceGroupResources(ResourceGroup resourceGroup)
        {
            return await ArmHttp<ArmArrayWrapper<object>>(HttpMethod.Get, ArmUriTemplates.ResourceGroupResources.Bind(resourceGroup));
        }

        public async Task<ResourceGroup> CreateResourceGroup(ResourceGroup resourceGroup)
        {
            await ArmHttp(HttpMethod.Put, ArmUriTemplates.ResourceGroup.Bind(resourceGroup), new { properties = new { }, location = resourceGroup.Location });
            return resourceGroup;
        }

        public async Task<ResourceGroup> EnsureResourceGroup(ResourceGroup resourceGroup)
        {
            var rgResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.ResourceGroup.Bind(resourceGroup));

            return rgResponse.IsSuccessStatusCode
                ? resourceGroup
                : await CreateResourceGroup(resourceGroup);
        }
    }
}