// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Reflection;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostStatus
    {
        /// <summary>
        /// Gets the host version.
        /// </summary>
        [JsonProperty(PropertyName = "version")]
        public static readonly string Version = GetAssemblyFileVersion(Assembly.GetExecutingAssembly());

        /// <summary>
        /// Gets or sets the collection of errors for the host.
        /// </summary>
        [JsonProperty(PropertyName = "errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Collection<string> Errors { get; set; }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? "Unknown";
        }
    }
}