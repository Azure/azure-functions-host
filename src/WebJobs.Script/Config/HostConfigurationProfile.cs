// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class HostConfigurationProfile
    {
        public static readonly HostConfigurationProfile Default = new(
            string.Empty, ImmutableDictionary<string, string>.Empty);

        public static readonly HostConfigurationProfile Mcp = new(
            "mcp", new Dictionary<string, string>
            {
                [ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "customHandler", "enableHttpProxyingRequest")] = "true",
                [ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "http", "routePrefix")] = string.Empty,
            });

        private HostConfigurationProfile(
            string name,
            IReadOnlyDictionary<string, string> configuration)
        {
            Name = name;
            Configuration = configuration;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Configuration { get; }

        public static HostConfigurationProfile FromName(string name)
        {
            if (TryGet(name, out HostConfigurationProfile profile))
            {
                return profile;
            }

            throw new ArgumentException(
                $"Unknown configuration profile '{name}'. Allowed values are '', 'default', 'mcp'.",
                nameof(name));
        }

        public static bool TryGet(string name, out HostConfigurationProfile profile)
        {
            ArgumentNullException.ThrowIfNull(name);
            profile = name?.ToLowerInvariant() switch
            {
                "mcp" => Mcp,
                "" or "default" => Default,
                _ => null,
            };

            return profile is not null;
        }
    }
}