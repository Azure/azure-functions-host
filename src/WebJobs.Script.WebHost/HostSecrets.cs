// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class HostSecrets
    {
        [JsonProperty(PropertyName = "masterKey")]
        public string MasterKey { get; set; }
        [JsonProperty(PropertyName = "functionKey")]
        public string FunctionKey { get; set; }
    }
}