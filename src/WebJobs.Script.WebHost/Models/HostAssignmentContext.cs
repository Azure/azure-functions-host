// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostAssignmentContext
    {
        [JsonProperty("siteId")]
        public int SiteId { get; set; }

        [JsonProperty("siteName")]
        public string SiteName { get; set; }

        [JsonProperty("environment")]
        public Dictionary<string, string> Environment { get; set; }

        [JsonProperty("lastModifiedTime")]
        public DateTime LastModifiedTime { get; set; }

        [JsonProperty("MSISpecializationPayload")]
        public MSIContext MSIContext { get; set; }

        [JsonProperty("EncryptedTokenServiceSpecializationPayload")]
        public string EncryptedTokenServiceSpecializationPayload { get; set; }

        [JsonProperty("TokenServiceApiEndpoint")]
        public string TokenServiceApiEndpoint { get; set; }

        [JsonProperty("CorsSpecializationPayload")]
        public CorsSettings CorsSettings { get; set; }

        [JsonProperty("EasyAuthSpecializationPayload")]
        public EasyAuthSettings EasyAuthSettings { get; set; }

        [JsonProperty("Secrets")]
        public FunctionAppSecrets Secrets { get; set; }

        // This will be true for dummy specialization calls to pre-jit specialization code.
        // For warmup requests the Run-From-Pkg appsetting will point to a local endpoint that
        // returns 200 to ensure downloading app contents succeeds.
        // All the other fields will be empty.
        [JsonProperty("isWarmupRequest")]
        public bool IsWarmupRequest { get; set; }

        public long? PackageContentLength { get; set; }

        public string AzureFilesConnectionString
            => Environment.ContainsKey(EnvironmentSettingNames.AzureFilesConnectionString)
                ? Environment[EnvironmentSettingNames.AzureFilesConnectionString]
                : string.Empty;

        public string AzureFilesContentShare
            => Environment.ContainsKey(EnvironmentSettingNames.AzureFilesContentShare)
                && !string.IsNullOrEmpty(Environment[EnvironmentSettingNames.AzureFilesContentShare])
                ? Environment[EnvironmentSettingNames.AzureFilesContentShare]
                : SiteName;

        public bool IsAzureFilesContentShareConfigured(ILogger logger)
        {
            logger.LogDebug(
                $"{nameof(EnvironmentSettingNames.AzureFilesConnectionString)} IsNullOrEmpty: {string.IsNullOrEmpty(AzureFilesConnectionString)}. {nameof(EnvironmentSettingNames.AzureFilesContentShare)}: IsNullOrEmpty {string.IsNullOrEmpty(AzureFilesContentShare)}");
            return !string.IsNullOrEmpty(AzureFilesConnectionString) && !string.IsNullOrEmpty(AzureFilesContentShare);
        }

        public RunFromPackageContext GetRunFromPkgContext()
        {
            if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteRunFromPackage))
            {
                return new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteRunFromPackage,
                    Environment[EnvironmentSettingNames.AzureWebsiteRunFromPackage],
                    PackageContentLength, IsWarmupRequest);
            }
            else if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteAltZipDeployment))
            {
                return new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteAltZipDeployment,
                    Environment[EnvironmentSettingNames.AzureWebsiteAltZipDeployment],
                    PackageContentLength, IsWarmupRequest);
            }
            else if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteZipDeployment))
            {
                return new RunFromPackageContext(EnvironmentSettingNames.AzureWebsiteZipDeployment,
                    Environment[EnvironmentSettingNames.AzureWebsiteZipDeployment],
                    PackageContentLength, IsWarmupRequest);
            }
            else if (Environment.ContainsKey(EnvironmentSettingNames.ScmRunFromPackage))
            {
                return new RunFromPackageContext(EnvironmentSettingNames.ScmRunFromPackage,
                    Environment[EnvironmentSettingNames.ScmRunFromPackage],
                    PackageContentLength, IsWarmupRequest);
            }
            else
            {
                return new RunFromPackageContext(string.Empty, string.Empty, PackageContentLength, IsWarmupRequest);
            }
        }

        public IEnumerable<KeyValuePair<string, string>> GetBYOSEnvironmentVariables()
        {
            return Environment.Where(kv =>
                kv.Key.StartsWith(AzureStorageInfoValue.AzureFilesStoragePrefix, StringComparison.OrdinalIgnoreCase) ||
                kv.Key.StartsWith(AzureStorageInfoValue.AzureBlobStoragePrefix, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsMSIEnabled(out string endpoint)
        {
            endpoint = null;
            if (Environment.TryGetValue(EnvironmentSettingNames.MsiEndpoint, out endpoint))
            {
                string secret;
                if (Environment.TryGetValue(EnvironmentSettingNames.MsiSecret, out secret))
                {
                    return !string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(secret);
                }
            }

            return false;
        }

        public bool Equals(HostAssignmentContext other)
        {
            if (other == null)
            {
                return false;
            }

            return SiteId == other.SiteId && LastModifiedTime.CompareTo(other.LastModifiedTime) == 0;
        }

        public void ApplyAppSettings(IEnvironment environment, ILogger logger)
        {
            foreach (var pair in Environment)
            {
                environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
            if (CorsSettings != null)
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.CorsSupportCredentials, CorsSettings.SupportCredentials.ToString());

                if (CorsSettings.AllowedOrigins != null)
                {
                    var allowedOrigins = JsonConvert.SerializeObject(CorsSettings.AllowedOrigins);
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.CorsAllowedOrigins, allowedOrigins);
                }
            }

            if (EasyAuthSettings != null)
            {
                // App settings take precedence over site config for easy auth enabled.
                if (string.IsNullOrEmpty(environment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthEnabled)))
                {
                    logger.LogDebug($"ApplyAppSettings is adding {EnvironmentSettingNames.EasyAuthEnabled} = {EasyAuthSettings.SiteAuthEnabled.ToString()}");
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.EasyAuthEnabled, EasyAuthSettings.SiteAuthEnabled.ToString());
                }
                else
                {
                    logger.LogDebug($"ApplyAppSettings operating on existing {EnvironmentSettingNames.EasyAuthEnabled} = {environment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthEnabled)}");
                }
                environment.SetEnvironmentVariable(EnvironmentSettingNames.EasyAuthClientId, EasyAuthSettings.SiteAuthClientId);
            }
        }
    }
}
