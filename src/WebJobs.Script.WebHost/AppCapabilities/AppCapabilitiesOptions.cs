// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.AppCapabilities
{
    public sealed class AppCapabilitiesOptions
    {
        // Map: capability name -> definition
        // case-insensitive to avoid issues collecting from multiple sources/components
        public Dictionary<string, CapabilityDefinition> Capabilities { get; init; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CapabilityDefinition
    {
        public string? Source { get; init; } // "host" | "worker:<lang>" | "extension:<pkg>" | "config" | "environment"

        public string? Version { get; init; }

        public Dictionary<string, object?> Metadata { get; init; } = new();
    }
}