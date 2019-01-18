// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensionBundle
{
    public class ExtensionBundleOptions
    {
        public string Id { get; set; }

        public VersionRange Version { get; set; }
    }
}