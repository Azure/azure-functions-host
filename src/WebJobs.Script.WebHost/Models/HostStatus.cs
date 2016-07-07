// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Reflection;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostStatus
    {
        public HostStatus()
        {
            Version = ScriptHost.Version;
        }

        /// <summary>
        /// Gets or sets the host id.
        /// </summary>
        [JsonProperty(PropertyName = "id", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the host version.
        /// </summary>
        [JsonProperty(PropertyName = "version", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the collection of errors for the host.
        /// </summary>
        [JsonProperty(PropertyName = "errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Collection<string> Errors { get; set; }

        /// <summary>
        /// Gets or sets the web host settings.
        /// </summary>
        [JsonProperty(PropertyName = "webHostSettings", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public WebHostSettings WebHostSettings { get; set; }

        /// <summary>
        /// Gets or sets the processId for the host.
        /// </summary>
        [JsonProperty(PropertyName = "processId", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int ProcessId { get; set; }

        /// <summary>
        /// Gets or sets whether the process has a debugger attached or not.
        /// </summary>
        [JsonProperty(PropertyName = "isDebuggerAttached", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsDebuggerAttached { get; set; }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? "Unknown";
        }
    }
}
