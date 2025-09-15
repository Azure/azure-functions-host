// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class HostConfigurationProfile
    {
        public const string SectionKey = "configurationProfile";

        // Make sure to update this as new profiles are added.
        private const string SupportedValues = "'', 'default', 'mcp'";

        public static readonly HostConfigurationProfile Default = new("default", []);

        public static readonly HostConfigurationProfile Mcp = new(
            "mcp",
            [
                KeyValuePair.Create(ConfigurationPath.Combine(
                    ConfigurationSectionNames.CustomHandler, "enableHttpProxyingRequest"), "true"),
                KeyValuePair.Create(ConfigurationPath.Combine(
                    ConfigurationSectionNames.Http, "routePrefix"), string.Empty),
            ]);

        private HostConfigurationProfile(
            string name,
            IEnumerable<KeyValuePair<string, string>> configuration)
        {
            Name = name;
            Configuration = configuration.Append(KeyValuePair.Create(SectionKey, name));
        }

        public string Name { get; }

        public IEnumerable<KeyValuePair<string, string>> Configuration { get; }

        public static HostConfigurationProfile Get(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.ToLowerInvariant() switch
            {
                "mcp" => Mcp,
                "" or "default" => Default,
                _ => throw new NotSupportedException(
                        $"Configuration profile '{name}' is not supported. Supported values: {SupportedValues}."),
            };
        }
    }
}
