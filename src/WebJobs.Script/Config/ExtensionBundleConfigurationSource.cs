// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ExtensionBundleConfigurationSource : IConfigurationSource
    {
        private readonly string _scriptRoot;

        public ExtensionBundleConfigurationSource(string scriptRoot)
        {
            _scriptRoot = scriptRoot;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ExtensionBundleConfigurationProvider(this);
        }

        private class ExtensionBundleConfigurationProvider : ConfigurationProvider
        {
            private readonly ExtensionBundleConfigurationSource _configurationSource;

            public ExtensionBundleConfigurationProvider(ExtensionBundleConfigurationSource configurationSource)
            {
                _configurationSource = configurationSource;
            }

            public override void Load()
            {
                string hostFilePath = Path.Combine(_configurationSource._scriptRoot, ScriptConstants.HostMetadataFileName);
                JObject configObject = LoadConfig(hostFilePath);
                var bundleConfig = configObject?[ConfigurationSectionNames.ExtensionBundle];
                if (bundleConfig == null)
                {
                    return;
                }

                if (bundleConfig.Type == JTokenType.Object)
                {
                    Data[ConfigurationSectionNames.ExtensionBundleId] = bundleConfig?["id"]?.Value<string>();
                    Data[ConfigurationSectionNames.ExtensionBundleVersion] = bundleConfig?["version"]?.Value<string>();
                }
                else
                {
                    Data[ConfigurationSectionNames.JobHostExtensionBundle] = string.Empty;
                }
            }

            internal JObject LoadConfig(string configFilePath)
            {
                JObject configObject;
                try
                {
                    string json = File.ReadAllText(configFilePath);
                    configObject = JObject.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new FormatException($"Unable to parse Extension Bundle configuration '{configFilePath}'.", ex);
                }

                return configObject;
            }
        }
    }
}
