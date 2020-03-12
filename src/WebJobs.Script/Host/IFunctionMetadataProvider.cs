// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IFunctionMetadataProvider
    {
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh = false);
    }
}