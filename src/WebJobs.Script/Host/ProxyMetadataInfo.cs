// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ProxyMetadataInfo
    {
        public ProxyMetadataInfo(ImmutableArray<FunctionMetadata> functions, ImmutableDictionary<string, ImmutableArray<string>> errors, ProxyClientExecutor client)
        {
            Functions = functions;
            Errors = errors;
            ProxyClient = client;
        }

        public ImmutableArray<FunctionMetadata> Functions { get; }

        public ImmutableDictionary<string, ImmutableArray<string>> Errors { get; }

        public ProxyClientExecutor ProxyClient { get; }
    }
}
