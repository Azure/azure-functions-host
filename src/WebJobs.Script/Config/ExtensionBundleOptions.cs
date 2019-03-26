// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ExtensionBundleOptions
    {
        /// <summary>
        /// Gets or Sets the Id of the extension bundle
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or Sets the version range or version of the extension bundle.
        /// </summary>
        public VersionRange Version { get; set; }

        /// <summary>
        /// Gets the Probing path of the Extension Bundle.
        /// Probing path are configured by the host depending on the hosting enviroment the default location where the runtime would look for an extension bundle first.
        /// To be configured by the host or consuming service
        /// </summary>
        public ICollection<string> ProbingPaths { get; private set; } = new Collection<string>();

        /// <summary>
        /// Gets or Sets the download path for the extension bundle.
        /// This is the path where the runtime would download the extension bundle in case it is not present at the probing path.
        /// To be configured by the host or consuming service
        /// </summary>
        public string DownloadPath { get; set; }

        /// <summary>
        /// Gets or Sets a value indicating whether the runtime should force fetch the latest version of extension bundle available on CDN, even when there is a matching extension bundle available locally.
        /// To be configured by the host or consuming service
        /// </summary>
        public bool EnsureLatest { get; set; }
    }
}