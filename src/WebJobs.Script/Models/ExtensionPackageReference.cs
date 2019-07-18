// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Models
{
    [Flags]
    public enum ExtensionPostInstallActions
    {
        None = 0,
        BringAppOnline = 1 << 0,
    }

    /// <summary>
    /// Represents a binding extension package reference.
    /// </summary>
    public class ExtensionPackageReference
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

    /// <summary>
    /// Represents a binding extension package reference with additional flags that are applicable post installation.
    /// </summary>
    public class ExtensionPackageReferenceWithActions : ExtensionPackageReference
    {
        public string PostInstallActions { get; set; }
    }
}
