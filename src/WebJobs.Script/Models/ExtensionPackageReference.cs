// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Models
{
    /// <summary>
    /// Represents a binding extension package reference.
    /// </summary>
    public class ExtensionPackageReference : ExtensionPackageRetrieve
    {
        public bool DeferAppActivationToServer { get; set; } = false;
    }

    public class ExtensionPackageRetrieve
    {
        /// <summary>
        /// Gets or sets the referenced package ID.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the referenced package version string.
        /// This should contain the full packge version as defined in the package,
        /// including major, minor, patch and pre-release tag (e.g. 1.0.0-beta1).
        /// This may also contain a floating version string.
        /// </summary>
        public string Version { get; set; }
    }
}
