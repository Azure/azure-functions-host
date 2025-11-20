// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public sealed class HostConfigurationProfile
    {
        private const string SectionKey = "configurationProfile";

        // note: profile name consts are intentionally private.
        // This ensures tests will fail if these values are changed without updating the test also.
        private const string DefaultProfile = "default";

        private const string McpCustomHandlerProfile = "mcp-custom-handler";

        private const string WebAppCustomHandlerProfile = "web-app-custom-handler";

        // Make sure to update this as new profiles are added.
        private const string SupportedValues = $"'', '{DefaultProfile}', '{McpCustomHandlerProfile}', '{WebAppCustomHandlerProfile}'";

        private static readonly Dictionary<string, string> CommonHttpCustomHandlerConfiguration = new()
        {
            [ConfigurationPath.Combine(ConfigurationSectionNames.CustomHandler, ScriptConstants.EnableProxyingHttpRequest)] = "true",
            [ConfigurationPath.Combine(ConfigurationSectionNames.Http, "routePrefix")] = string.Empty,
            [ConfigurationPath.Combine(ConfigurationSectionNames.CustomHandler, "http", "routes", "0", "route")] = "{*route}"
        };

        public static readonly HostConfigurationProfile Default = new(DefaultProfile, []);

        public static readonly HostConfigurationProfile McpCustomHandler = new(McpCustomHandlerProfile, CommonHttpCustomHandlerConfiguration);

        public static readonly HostConfigurationProfile WebAppCustomHandler = new(WebAppCustomHandlerProfile, CommonHttpCustomHandlerConfiguration);

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
                McpCustomHandlerProfile => McpCustomHandler,
                WebAppCustomHandlerProfile => WebAppCustomHandler,
                "" or DefaultProfile => Default,
                _ => throw new NotSupportedException(
                        $"Configuration profile '{name}' is not supported. Supported values: {SupportedValues}."),
            };
        }
    }
}
