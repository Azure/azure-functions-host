// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.ExtensionRequirements
{
    internal sealed class ExtensionStartupTypeRequirement
    {
        // The loaded startup type must match this name for this requirement to be enforced.
        public string Name { get; set; }

        // The loaded startup type must come from this assembly for this requirement to be enforced.
        public string AssemblyName { get; set; }

        // If this requirement is enforced based on a match for Name and AssemblyName, then the assembly that contains the startup type must be of this version or greater.
        public string MinimumAssemblyVersion { get; set; }

        // Optional requirement - if present, this requirement is enforced. It is used to handle extensions like durable functions that do not bump assembly version on minor releases.
        public string MinimumAssemblyFileVersion { get; set; }

        // This is not part of the enforced requirements. It is only used to generate an error message that refers to the correct NuGet package, in case the name of the assembly that contains the startup type does not match the package name.
        public string PackageName { get; set; }

        // Like PackageName, this is not part of the enforced requirements. Its used to generate an error message that refers to the correct NuGet package version.
        public string MinimumPackageVersion { get; set; }
    }
}
