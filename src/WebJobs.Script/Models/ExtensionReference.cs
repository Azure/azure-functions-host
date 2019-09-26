// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Models
{
    /// <summary>
    /// Represents a binding extension reference.
    /// </summary>
    public class ExtensionReference
    {
        /// <summary>
        /// Gets or sets the extension name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the assembly-qualified name of the type.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Gets or sets a hit path that may be used when loading the assembly containing the extension
        /// implementation.
        /// </summary>
        public string HintPath { get; set; }

        /// <summary>
        /// Gets the binding exposed by the extension
        /// </summary>
        public ICollection<string> Bindings { get; } = new Collection<string>();
    }
}
