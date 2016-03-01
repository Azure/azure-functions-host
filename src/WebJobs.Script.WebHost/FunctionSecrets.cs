// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace WebJobs.Script.WebHost
{
    public class FunctionSecrets
    {
        [JsonProperty(PropertyName = "key")]
        public string Key { get; set; }
    }
}