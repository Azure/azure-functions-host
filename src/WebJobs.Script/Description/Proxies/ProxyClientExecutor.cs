// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client.Contract;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal static class ProxyClientExecutor
    {
        internal static async Task ExecuteProxyRequest(IProxyClient proxyClient, HttpRequestMessage request, ILogger logger)
        {
            await proxyClient.CallAsync(new object[] { request }, null, logger);
        }
    }
}
