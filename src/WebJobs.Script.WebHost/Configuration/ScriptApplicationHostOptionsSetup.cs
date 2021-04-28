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

        public ScriptApplicationHostOptionsSetup(IConfiguration configuration, IOptionsMonitor<StandbyOptions> standbyOptions, IOptionsMonitorCache<ScriptApplicationHostOptions> cache,
            IServiceProvider serviceProvider, IEnvironment environment, CloudBlockBlobHelperService cloudBlockBlobHelperService = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _serviceProvider = serviceProvider;
            // If standby options change, invalidate this options cache.
            _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => _cache.Clear());
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
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

            options.IsFileSystemReadOnly = IsZipDeployment(options);
        }

        private bool BlobExists()
        {
            return _cloudBlockBlobHelperService.BlobExists(_environment.GetEnvironmentVariable(ScmRunFromPackage));
        }

        private bool IsZipDeployment(ScriptApplicationHostOptions options)
        {
            // If the app is using app settings for run from package, we don't need to check further, it must be a zip deployment.
            bool runFromPkgConfigured = IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage));

            if (runFromPkgConfigured)
            {
                return true;
            }

            // If SCM_RUN_FROM_PACKAGE is set to a valid value and the blob exists, it's a zip deployment.
            // We need to explicitly check if the blob exists because on Linux Consumption the app setting is always added, regardless if it's used or not.
            bool scmRunFromPkgConfigured = IsValidZipSetting(_environment.GetEnvironmentVariable(ScmRunFromPackage)) && BlobExists();
            options.IsScmRunFromPackage = scmRunFromPkgConfigured;

            return scmRunFromPkgConfigured;
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
