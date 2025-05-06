// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Models
{
    /// <summary>
    /// Represents a binding extension reference.
    /// </summary>
    public sealed class ExtensionReference
    {
        /// <summary>
        /// Gets the extension name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the assembly-qualified name of the type.
        /// </summary>
        public string TypeName { get; init; }

        /// <summary>
        /// Gets a hit path that may be used when loading the assembly containing the extension.
        /// implementation.
        /// </summary>
        public string HintPath { get; init; }

        /// <summary>
        /// Gets the binding exposed by the extension.
        /// </summary>
        public ICollection<string> Bindings { get; init; } = [];
    }
}
