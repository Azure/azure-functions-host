// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.BindingExtensionBundle
{
    internal class ExtensionBundleOptionsSetup : IConfigureOptions<ExtensionBundleOptions>
    {
        private readonly IConfiguration _configuration;

        public ExtensionBundleOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(ExtensionBundleOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var extensionBundleSection = jobHostSection.GetSection(ConfigurationSectionNames.ExtensionBundle);
            extensionBundleSection.Bind(options);

            if (extensionBundleSection.Exists())
            {
                ValidateBundleId(options.Id);
                ConfigureBundleVersion(extensionBundleSection, options);
            }
        }

        private static void ConfigureBundleVersion(IConfigurationSection configurationSection, ExtensionBundleOptions options)
        {
            string bundleVersion = configurationSection.GetValue<string>("version");
            if (string.IsNullOrWhiteSpace(bundleVersion) || !VersionRange.TryParse(bundleVersion.ToString(), out VersionRange version))
            {
                string message = string.Format(Resources.ExtensionBundleConfigMissingVersion, ScriptConstants.HostMetadataFileName);
                throw new ArgumentException(message);
            }
            options.Version = version;
        }

        private static void ValidateBundleId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !PackageIdValidator.IsValidPackageId(id))
            {
                string message = string.Format(Resources.ExtensionBundleConfigMissingId, ScriptConstants.HostMetadataFileName);
                throw new ArgumentException(message);
            }
        }
    }
}
