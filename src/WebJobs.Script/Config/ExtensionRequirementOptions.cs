// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.ExtensionRequirements;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ExtensionRequirementOptions
    {
        /// <summary>
        /// Gets or sets the minimum bundles configuration required for the function app.
        /// </summary>
        public IEnumerable<BundleRequirement> Bundles { get; set; }

        /// <summary>
        /// Gets or Sets the minimum versions of extensions required for the function app.
        /// </summary>
        public IEnumerable<ExtensionStartupTypeRequirement> Extensions { get; set; }
    }
}