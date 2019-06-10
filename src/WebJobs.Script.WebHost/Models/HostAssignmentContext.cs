// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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

        public string ZipUrl
        {
            get
            {
                if (ZipUrlEnvVar != string.Empty)
                {
                    return Environment[ZipUrlEnvVar];
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        public string ZipUrlEnvVar
        {
            get
            {
                if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteRunFromPackage))
                {
                    return EnvironmentSettingNames.AzureWebsiteRunFromPackage;
                }
                else if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteAltZipDeployment))
                {
                    return EnvironmentSettingNames.AzureWebsiteAltZipDeployment;
                }
                else if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteZipDeployment))
                {
                    return EnvironmentSettingNames.AzureWebsiteZipDeployment;
                }
                else if (Environment.ContainsKey(EnvironmentSettingNames.ScmRunFromPackage))
                {
                    return EnvironmentSettingNames.ScmRunFromPackage;
                }
                else
                {
                    return string.Empty;
                }
            }
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

        public void ApplyAppSettings(IEnvironment environment)
        {
            foreach (var pair in Environment)
            {
                environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
