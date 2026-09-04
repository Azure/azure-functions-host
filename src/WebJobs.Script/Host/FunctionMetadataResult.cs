// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Contains worker-provided function metadata and its indexing disposition.
    /// </summary>
    public sealed class FunctionMetadataResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionMetadataResult"/> class.
        /// </summary>
        /// <param name="useDefaultMetadataIndexing">Whether the host should use its default metadata indexing path.</param>
        /// <param name="functions">The validated worker-provided functions.</param>
        public FunctionMetadataResult(bool useDefaultMetadataIndexing, ImmutableArray<FunctionMetadata> functions)
        {
            UseDefaultMetadataIndexing = useDefaultMetadataIndexing;
            Functions = functions;
        }

        /// <summary>
        /// Gets a value indicating whether the host should use default metadata indexing.
        /// </summary>
        public bool UseDefaultMetadataIndexing { get; }

        /// <summary>
        /// Gets the validated worker-provided functions.
        /// </summary>
        public ImmutableArray<FunctionMetadata> Functions { get; }
    }
}