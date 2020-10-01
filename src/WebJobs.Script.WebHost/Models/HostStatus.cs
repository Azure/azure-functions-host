// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostStatus
    {
        /// <summary>
        /// Gets or sets the host id.
        /// </summary>
        [JsonProperty(PropertyName = "id", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the current status of the host.
        /// </summary>
        [JsonProperty(PropertyName = "state", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string State { get; set; }

        /// <summary>
        /// Gets or sets the host version.
        /// </summary>
        [JsonProperty(PropertyName = "version", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the host version details. This provides the host informational version.
        /// </summary>
        [JsonProperty(PropertyName = "versionDetails", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string VersionDetails { get; set; }

        /// <summary>
        /// Gets or sets the Antares version. This corresponds to platform_version on linux and website_platform_version on windows.
        /// </summary>
        [JsonProperty(PropertyName = "platformVersion", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string PlatformVersion { get; set; }

        /// <summary>
        /// Gets or sets the machine identifier the host is running on. This corresponds to WEBSITE_INSTANCE_ID.
        /// </summary>
        [JsonProperty(PropertyName = "instanceId", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string InstanceId { get; set; }

        /// <summary>
        /// Gets or sets the machine name the host is running on. This corresponds to COMPUTERNAME environment variable.
        /// </summary>
        [JsonProperty(PropertyName = "computerName", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string ComputerName { get; set; }

        /// <summary>
        /// Gets or sets the collection of errors for the host.
        /// </summary>
        [JsonProperty(PropertyName = "errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Collection<string> Errors { get; set; }

        /// <summary>
        /// Gets or sets the uptime of the process
        /// </summary>
        [JsonProperty(PropertyName = "processUptime", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long ProcessUptime { get; set; }

        /// <summary>
        /// Gets or sets the information related to Extension bundles
        /// </summary>
        [JsonProperty(PropertyName = "extensionBundle", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ExtensionBundle ExtensionBundle { get; set; }
    }
}
