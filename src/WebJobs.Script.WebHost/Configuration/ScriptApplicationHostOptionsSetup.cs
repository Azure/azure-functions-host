// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
        private readonly CloudBlockBlobHelperService _cloudBlockBlobHelperService;
        private ILogger _logger;

        public ScriptApplicationHostOptionsSetup(IConfiguration configuration, IOptionsMonitor<StandbyOptions> standbyOptions, IOptionsMonitorCache<ScriptApplicationHostOptions> cache,
            IServiceProvider serviceProvider, IEnvironment environment, ILogger<ScriptApplicationHostOptionsSetup> logger, CloudBlockBlobHelperService cloudBlockBlobHelperService = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _serviceProvider = serviceProvider;
            // If standby options change, invalidate this options cache.
            _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => _cache.Clear());
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger;
            _cloudBlockBlobHelperService = cloudBlockBlobHelperService ?? new CloudBlockBlobHelperService();
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

            options.AreZipDeploymentAppSettingsValid = ValidateZipDeploymentAppSettings();
            options.ScmRunFromPackageBlobExists = BlobExists();
            options.IsZipDeployment = IsZipDeployment(options);
            options.IsFileSystemReadOnly = options.IsZipDeployment;
        }

        private bool BlobExists()
        {
            var blobExists = _cloudBlockBlobHelperService.BlobExists(_environment.GetEnvironmentVariable(ScmRunFromPackage), EnvironmentSettingNames.ScmRunFromPackage, _logger).GetAwaiter().GetResult();
            _logger.LogInformation($"Checked if ${EnvironmentSettingNames.ScmRunFromPackage} points to an existing blob: ${blobExists}");
            return blobExists;
        }

        private bool IsZipDeployment(ScriptApplicationHostOptions options)
        {
            // If the app is using the new app settings for zip deployment, we don't need to check further, it must be a zip deployment.
            bool usesNewZipDeployAppSettings = IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage));

            if (usesNewZipDeployAppSettings)
            {
                return usesNewZipDeployAppSettings;
            }

            // Check if the old app setting for zip deployment is set, SCM_RUN_FROM_PACKAGE.
            // If not, this is not a zip deployment. If yes, we still need to check if the blob exists,
            // since this app setting is always created for Linux Consumption, even when it is not used.
            // In portal deployment, SCM_RUN_FROM_PACKAGE will be set, but we will be using Azure Files instead.
            if (!IsValidZipUrl(_environment.GetEnvironmentVariable(ScmRunFromPackage)))
            {
                return false;
            }

            // If SCM_RUN_FROM_PACKAGE is a valid app setting and Azure Files are not being used, it must be a zip deployment.
            if (string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureFilesConnectionString)) &&
                string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureFilesContentShare)))
            {
                return true;
            }

            // SCM_RUN_FROM_PACKAGE is set, as well as Azure Files app settings, so we need to check if we are actually using the zip blob.
            return options.ScmRunFromPackageBlobExists;
        }

        private bool ValidateZipDeploymentAppSettings()
        {
            return !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                   !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                   !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage)) ||
                   !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(ScmRunFromPackage));
        }

        private static bool IsValidZipSetting(string appSetting)
        {
            // valid values are 1 or an absolute URI
            return string.Equals(appSetting, "1") || IsValidZipUrl(appSetting);
        }

        private static bool IsValidZipUrl(string appSetting)
        {
            return Uri.TryCreate(appSetting, UriKind.Absolute, out Uri result);
        }

        public void Dispose()
        {
            _standbyOptionsOnChangeSubscription?.Dispose();
        }
    }
}
