// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.ExtensionRequirements
{
    internal sealed class BundleRequirement
    {
        public string Id { get; set; }

        public string MinimumVersion { get; set; }
    }
}
