// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ExtensionBundleOptions
    {
        public string Id { get; set; }

        public VersionRange Version { get; set; }

        public ICollection<string> ProbingPaths { get; private set; } = new List<string>();

        public string DownloadPath { get; set; }

        public bool EnsureLatest { get; set; }
    }
}