// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public sealed class HostConfigurationProfile
    {
        public const string SectionKey = "configurationProfile";

        // note: profile name consts are intentionally private.
        // This ensures tests will fail if these values are changed without updating the test also.
        private const string DefaultProfile = "default";

        private const string McpCustomerHandlerProfile = "mcp-custom-handler";

        // Make sure to update this as new profiles are added.
        private const string SupportedValues = $"'', '{DefaultProfile}', '{McpCustomerHandlerProfile}'";

        public static readonly HostConfigurationProfile Default = new(DefaultProfile, []);

        public static readonly HostConfigurationProfile McpCustomHandler = new(
            McpCustomerHandlerProfile,
            [
                KeyValuePair.Create(ConfigurationPath.Combine(
                    ConfigurationSectionNames.CustomHandler, "enableHttpProxyingRequest"), "true"),
                KeyValuePair.Create(ConfigurationPath.Combine(
                    ConfigurationSectionNames.Http, "routePrefix"), string.Empty),
                KeyValuePair.Create(ConfigurationPath.Combine(
                    ConfigurationSectionNames.CustomHandler, "http", "routes", "0", "route"), "{*route}"),
            ]);

        private HostConfigurationProfile(
            string name,
            IEnumerable<KeyValuePair<string, string>> configuration)
        {
            Name = name;
            Configuration = [.. configuration, KeyValuePair.Create(SectionKey, name)];
        }

        public string Name { get; }

        public IEnumerable<KeyValuePair<string, string>> Configuration { get; }

        public static HostConfigurationProfile Get(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.ToLowerInvariant() switch
            {
                McpCustomerHandlerProfile => McpCustomHandler,
                "" or DefaultProfile => Default,
                _ => throw new NotSupportedException(
                        $"Configuration profile '{name}' is not supported. Supported values: {SupportedValues}."),
            };
        }
    }
}
