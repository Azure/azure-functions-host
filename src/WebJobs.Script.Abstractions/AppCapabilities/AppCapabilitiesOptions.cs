// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class AppCapabilitiesOptions
    {
        public Dictionary<string, string> Capabilities { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}