// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyFunctionMetadata : FunctionMetadata
    {
        public ProxyFunctionMetadata(ProxyClientExecutor proxyClient)
        {
            ProxyClient = proxyClient;
        }

        public ProxyClientExecutor ProxyClient { get; }
    }
}
