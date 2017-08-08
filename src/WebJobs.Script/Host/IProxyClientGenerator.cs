// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IProxyClientGenerator
    {
        /// <summary>
        /// The return type is object to facilitate dynamic loading of Proxy dlls
        /// </summary>
        object CreateProxyClient(string proxyJson, ILogger logger);
    }
}
