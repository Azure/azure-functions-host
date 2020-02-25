// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    /// <summary>
    /// Secrets cache format used by both <see cref="FunctionsSyncManager"/> and <see cref="StartupContextProvider"/>.
    /// </summary>
    public class FunctionAppSecrets
    {
        [JsonProperty("host")]
        public HostSecrets Host { get; set; }

        [JsonProperty("function")]
        public FunctionSecrets[] Function { get; set; }

        public class FunctionSecrets
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("secrets")]
            public IDictionary<string, string> Secrets { get; set; }
        }

        public class HostSecrets
        {
            [JsonProperty("master")]
            public string Master { get; set; }

            [JsonProperty("function")]
            public IDictionary<string, string> Function { get; set; }

            [JsonProperty("system")]
            public IDictionary<string, string> System { get; set; }
        }
    }
}
