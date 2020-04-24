// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyFunctionMetadata : FunctionMetadata
    {
        public ProxyFunctionMetadata(ProxyClientExecutor proxyClient)
        {
            ProxyClient = proxyClient;

            this.SetIsCodeless(true);
        }

        public ProxyClientExecutor ProxyClient { get; }
    }
}
