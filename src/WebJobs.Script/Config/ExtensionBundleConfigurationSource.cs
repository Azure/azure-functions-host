// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ExtensionBundleConfigurationSource : FileConfigurationSource
    {
        private const string IdProperty = "id";
        private const string VersionProperty = "version";

        public bool IsAppServiceEnvironment { get; set; }

        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            if (IsAppServiceEnvironment)
            {
                string home = GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                Path = System.IO.Path.Combine(home, "site", "wwwroot", ScriptConstants.HostMetadataFileName);
            }
            else
            {
                string root = GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsScriptRoot);
                Path = System.IO.Path.Combine(root, ScriptConstants.HostMetadataFileName);
            }

            ReloadOnChange = true;
            ResolveFileProvider();
            return new ExtensionBundleConfigurationProvider(this);
        }

        public class ExtensionBundleConfigurationProvider : FileConfigurationProvider
        {
            public ExtensionBundleConfigurationProvider(ExtensionBundleConfigurationSource configurationSource) : base(configurationSource) { }

            public override void Load(Stream stream)
            {
                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    JObject configObject = JObject.Parse(json);

                    var bundleConfig = configObject?[ConfigurationSectionNames.ExtensionBundle];
                    if (bundleConfig == null)
                    {
                        return;
                    }

                    var bundleSection = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, ConfigurationSectionNames.ExtensionBundle);
                    var idProperty = ConfigurationPath.Combine(bundleSection, IdProperty);
                    var versionProperty = ConfigurationPath.Combine(bundleSection, VersionProperty);

                    if (bundleConfig.Type != JTokenType.Object)
                    {
                        Data[bundleSection] = string.Empty;
                    }
                    else
                    {
                        Data[idProperty] = bundleConfig?[IdProperty]?.Value<string>();
                        Data[versionProperty] = bundleConfig?[VersionProperty]?.Value<string>();
                    }
                }
            }
        }
    }
}
