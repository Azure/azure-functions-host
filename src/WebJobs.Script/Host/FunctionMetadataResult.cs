// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class FunctionMetadataResult
    {
        public FunctionMetadataResult(bool useDefaultMetadataIndexing, ImmutableArray<FunctionMetadata> functions)
        {
            this.UseDefaultMetadataIndexing = useDefaultMetadataIndexing;
            this.Functions = functions;
        }

        public bool UseDefaultMetadataIndexing { get; private set; }

        public ImmutableArray<FunctionMetadata> Functions { get; private set; }
    }
}