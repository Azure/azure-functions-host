// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class FunctionSecrets
    {
        [JsonProperty(PropertyName = "key")]
        public string Key { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        [JsonProperty(PropertyName = "trigger_url")]
        public string TriggerUrl { get; set; }
    }
}