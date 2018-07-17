// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IFunctionMetadataManager
    {
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        ImmutableArray<FunctionMetadata> FunctionMetadata { get; }
    }
}