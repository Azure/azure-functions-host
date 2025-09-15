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
            "default", ImmutableDictionary<string, string>.Empty);

        public static readonly HostConfigurationProfile Mcp = new(
            "mcp",
            new Dictionary<string, string>
            {
                [ConfigurationPath.Combine("customHandler", "enableHttpProxyingRequest")] = "true",
                [ConfigurationPath.Combine("extensions", "http", "routePrefix")] = string.Empty,
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

        public static HostConfigurationProfile Get(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.ToLowerInvariant() switch
            {
                "mcp" => Mcp,
                "" or "default" => Default,
                _ => throw new ArgumentException(
                        $"Configuration profile '{name}' is not supported. Supported values: '', 'default', 'mcp'.",
                        nameof(name)),
            };
        }
    }
}
