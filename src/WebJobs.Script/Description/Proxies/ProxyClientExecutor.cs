﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.AppService.Proxy.Client.Contract;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ProxyClientExecutor
    {
        private IProxyClient _proxyClient;

        public ProxyClientExecutor(IProxyClient proxyClient)
        {
            _proxyClient = proxyClient;
        }

        public ProxyData GetProxyData()
        {
            return _proxyClient.GetProxyData();
        }

        public async Task Execute(HttpRequestMessage request, ILogger logger)
        {
            await _proxyClient.CallAsync(new object[] { request }, null, logger);
        }
    }
}
