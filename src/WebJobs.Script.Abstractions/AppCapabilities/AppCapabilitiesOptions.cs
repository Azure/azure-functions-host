// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class AppCapabilitiesOptions
    {
        // Map: capability name -> definition
        // case-insensitive to avoid issues collecting from multiple sources/components
        public Dictionary<string, CapabilityDefinition> Capabilities { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CapabilityDefinition
    {
        public string? Source { get; set; } // "host" | "worker:<lang>" | "extension:<pkg>" | "config" | "environment"

        public string? Version { get; set; }

        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}