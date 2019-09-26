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
