// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyClientGenerator : IProxyClientGenerator
    {
        /// <summary>
        /// The return type is object to facilitate dynamic loading of Proxy dlls
        /// </summary>
        public object CreateProxyClient(string proxyJson, ILogger logger)
        {
            return AppService.Proxy.Client.Contract.ProxyClientFactory.Create(proxyJson, logger);
        }
    }
}
