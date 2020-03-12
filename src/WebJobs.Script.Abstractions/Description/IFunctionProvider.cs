// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Description
{
    public interface IFunctionProvider
    {
        /// <summary>
        /// Gets any function errors that may occur as part of the provider context
        /// </summary>
        /// <returns> An ImmutableDictionary of function name to the array of errors</returns>
        ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors { get; }

        /// <summary>
        /// Gets all function metadata that this provider knows about asynchronously
        /// </summary>
        /// <returns>A Task with IEnumerable of FunctionMetadata</returns>
        Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync();
    }
}
