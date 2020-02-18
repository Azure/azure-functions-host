// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Abstractions
{
    public interface IFunctionProvider
    {
        /// <summary>
        /// Gets any function errors that may occur as part of the provider context
        /// </summary>
        /// <returns> An IDictionary of function name to the list of errors</returns>
        IDictionary<string, ICollection<string>> GetFunctionErrors();

        /// <summary>
        /// Gets all function metadata that this provider knows about
        /// </summary>
        /// <returns>An IEnumerable of FunctionMetadata</returns>
        IEnumerable<FunctionMetadata> GetFunctionMetadata();
    }
}
