// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    /// <summary>
    /// Shared helper methods for capability management.
    /// </summary>
    public static class AppCapabilityHelpers
    {
        /// <summary>
        /// Gets the precedence value for a capability source.
        /// </summary>
        public static int GetPrecedence(string? source)
        {
            return (source ?? string.Empty) switch
            {
                CapabilitySourceNames.ConfigSource => CapabilitySourcePrecedence.Config,
                CapabilitySourceNames.HostSource => CapabilitySourcePrecedence.Host,
                CapabilitySourceNames.WorkerSource => CapabilitySourcePrecedence.Worker,
                CapabilitySourceNames.ExtensionSource => CapabilitySourcePrecedence.Extension,
                _ => 0
            };
        }

        /// <summary>
        /// Determines whether a new capability source should override the current source.
        /// </summary>
        public static bool ShouldOverride(string? currentSource, string newSource)
        {
            int currentPrec = GetPrecedence(currentSource);
            int newPrec = GetPrecedence(newSource);
            return newPrec > currentPrec;
        }

        /// <summary>
        /// Merges metadata dictionaries, with incoming values overriding existing ones.
        /// </summary>
        public static Dictionary<string, string> MergeMetadata(
            IDictionary<string, string> existing,
            IReadOnlyDictionary<string, string>? incoming)
        {
            if (incoming is null || incoming.Count == 0)
            {
                return new Dictionary<string, string>(existing);
            }

            var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            foreach (var item in incoming)
            {
                merged[item.Key] = item.Value;
            }

            return merged;
        }

        /// <summary>
        /// Adds or updates a capability with precedence rules.
        /// </summary>
        public static void AddOrUpdateCapability(
            Dictionary<string, CapabilityDefinition> capabilities,
            string name,
            string source,
            string? version = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (!capabilities.TryGetValue(name, out var current))
            {
                capabilities[name] = new CapabilityDefinition
                {
                    Source = source,
                    Version = version,
                    Metadata = metadata?.ToDictionary(kv => kv.Key, kv => kv.Value)
                              ?? new Dictionary<string, string>()
                };
                return;
            }

            if (ShouldOverride(current.Source, source))
            {
                capabilities[name] = new CapabilityDefinition
                {
                    Source = source,
                    Version = version ?? current.Version,
                    Metadata = MergeMetadata(current.Metadata, metadata)
                };
            }
        }
    }
}