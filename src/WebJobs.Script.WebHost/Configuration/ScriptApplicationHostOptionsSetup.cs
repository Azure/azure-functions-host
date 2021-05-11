// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class ScriptApplicationHostOptionsSetup : IConfigureNamedOptions<ScriptApplicationHostOptions>, IDisposable
    {
        public const string SkipPlaceholder = "SkipPlaceholder";
        private readonly IOptionsMonitorCache<ScriptApplicationHostOptions> _cache;
        private readonly IConfiguration _configuration;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly IDisposable _standbyOptionsOnChangeSubscription;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEnvironment _environment;

        public ScriptApplicationHostOptionsSetup(IConfiguration configuration, IOptionsMonitor<StandbyOptions> standbyOptions,
            IOptionsMonitorCache<ScriptApplicationHostOptions> cache, IServiceProvider serviceProvider, IEnvironment environment)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _serviceProvider = serviceProvider;
            // If standby options change, invalidate this options cache.
            _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => _cache.Clear());
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public void Configure(ScriptApplicationHostOptions options)
        {
            Configure(null, options);
        }

        public void Configure(string name, ScriptApplicationHostOptions options)
        {
            _configuration.GetSection(ConfigurationSectionNames.WebHost)
                ?.Bind(options);

            // Indicate that a WebHost is hosting the ScriptHost
            options.HasParentScope = true;
            options.RootServiceProvider = _serviceProvider;

            // During assignment, we need a way to get the non-placeholder ScriptPath
            // while we are still in PlaceholderMode. This is a way for us to request it from the
            // OptionsFactory and still allow other setups to run.
            if (_standbyOptions.CurrentValue.InStandbyMode &&
                !string.Equals(name, SkipPlaceholder, StringComparison.Ordinal))
            {
                // If we're in standby mode, override relevant properties with values
                // to be used by the placeholder site.
                // Important that we use paths that are different than the configured paths
                // to ensure that placeholder files are isolated
                string tempRoot = Path.GetTempPath();

                options.LogPath = Path.Combine(tempRoot, @"functions\standby\logs");
                options.ScriptPath = Path.Combine(tempRoot, @"functions\standby\wwwroot");
                options.SecretsPath = Path.Combine(tempRoot, @"functions\standby\secrets");
                options.IsSelfHost = options.IsSelfHost;
                options.IsStandbyConfiguration = true;
            }

            options.IsFileSystemReadOnly = IsZipDeployment(options);
        }

        private bool IsZipDeployment(ScriptApplicationHostOptions options)
        {
            // Check app settings for run from package.
            bool runFromPkgConfigured = Utility.IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                Utility.IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                Utility.IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage));

            if (!_environment.IsLinuxConsumption())
            {
                // This check is strong enough for SKUs other than Linux Consumption.
                return runFromPkgConfigured;
            }

            // The following logic only applies to Linux Consumption, since currently SCM_RUN_FROM_PACKAGE is always set even if we are not using it.
            if (runFromPkgConfigured)
            {
                return true;
            }

            // If SCM_RUN_FROM_PACKAGE is set to a valid value and the blob exists, it's a zip deployment.
            var url = _environment.GetEnvironmentVariable(ScmRunFromPackage);
            if (string.IsNullOrEmpty(url))
            {
                LinuxContainerEventGenerator.LogInfo($"{nameof(ScmRunFromPackage)} is empty.");
            }
            else
            {
                LinuxContainerEventGenerator.LogInfo($"{nameof(ScmRunFromPackage)} = {url.Substring(0, 25)}");
            }

            var isValidZipSetting = Utility.IsValidZipSetting(url);
            LinuxContainerEventGenerator.LogInfo($"{nameof(ScmRunFromPackage)} isValidZipSetting = {isValidZipSetting}");

            if (!isValidZipSetting)
            {
                // Return early so we don't call storage if it isn't absolutely necessary.
                return false;
            }

            var blobExists = BlobExists(url);
            LinuxContainerEventGenerator.LogInfo($"{nameof(ScmRunFromPackage)} blobExists = {blobExists}");

            bool scmRunFromPkgConfigured = isValidZipSetting && blobExists;
            options.IsScmRunFromPackage = scmRunFromPkgConfigured;

            return scmRunFromPkgConfigured;
        }

        public virtual bool BlobExists(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            try
            {
                CloudBlockBlob blob = new CloudBlockBlob(new Uri(url));

                int attempt = 0;
                while (true)
                {
                    try
                    {
                        return blob.Exists();
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        LinuxContainerEventGenerator.LogInfo($"Exception when checking if {nameof(ScmRunFromPackage)} blob exists: {ex}");
                        if (++attempt > 2)
                        {
                            return false;
                        }
                        Thread.Sleep(TimeSpan.FromSeconds(0.3));
                    }
                }
            }
            catch (Exception)
            {
                LinuxContainerEventGenerator.LogInfo("BlobExists failed after retry");
                return false;
            }
        }

        public void Dispose()
        {
            _standbyOptionsOnChangeSubscription?.Dispose();
        }
    }
}
