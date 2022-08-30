// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Configuration;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class ExtensionBundleConfigurationHelper
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;

        public ExtensionBundleConfigurationHelper(IConfiguration configuration, IEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public void Configure(ExtensionBundleOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var extensionBundleSection = jobHostSection.GetSection(ConfigurationSectionNames.ExtensionBundle);

            if (extensionBundleSection.Exists())
            {
                ConfigureBundleVersion(extensionBundleSection, options);
                extensionBundleSection.Bind(options);
                ValidateBundleId(options.Id);

                string homeDirectory = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                if ((_environment.IsAppService()
                    || _environment.IsAnyLinuxConsumption()
                    || _environment.IsContainer())
                    && !string.IsNullOrEmpty(homeDirectory))
                {
                    options.DownloadPath = Path.Combine(homeDirectory, "data", "Functions", ScriptConstants.ExtensionBundleDirectory, options.Id);
                    ConfigureProbingPaths(options);
                }
            }
        }

        private void ConfigureBundleVersion(IConfigurationSection configurationSection, ExtensionBundleOptions options)
        {
            string bundleVersion = configurationSection.GetValue<string>("version");
            if (string.IsNullOrWhiteSpace(bundleVersion) || !VersionRange.TryParse(bundleVersion.ToString(), allowFloating: true, out VersionRange version))
            {
                string message = string.Format(Resources.ExtensionBundleConfigMissingVersion, ScriptConstants.HostMetadataFileName);
                throw new ArgumentException(message);
            }
            options.Version = version;
        }

        private void ValidateBundleId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !PackageIdValidator.IsValidPackageId(id))
            {
                string message = string.Format(Resources.ExtensionBundleConfigMissingId, ScriptConstants.HostMetadataFileName);
                throw new ArgumentException(message);
            }
        }

        private void ConfigureProbingPaths(ExtensionBundleOptions options)
        {
            if (_environment.IsWindowsAzureManagedHosting())
            {
                string windowsDefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                                         ScriptConstants.DefaultExtensionBundleDirectory,
                                                         options.Id);

                options.ProbingPaths.Add(windowsDefaultPath);
            }

            var homeDirectory = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            if ((_environment.IsLinuxAppService()
                || _environment.IsAnyLinuxConsumption()
                || _environment.IsContainer())
                && !string.IsNullOrEmpty(homeDirectory))
            {
                string linuxDefaultPath = Path.Combine(Path.GetPathRoot(homeDirectory), ScriptConstants.DefaultExtensionBundleDirectory, options.Id);
                string deploymentPackageBundlePath = Path.Combine(homeDirectory, "site", "wwwroot",
                                                                  ScriptConstants.AzureFunctionsSystemDirectoryName,
                                                                  ScriptConstants.ExtensionBundleDirectory, options.Id);

                options.ProbingPaths.Add(linuxDefaultPath);
                options.ProbingPaths.Add(deploymentPackageBundlePath);
            }
        }
    }
}