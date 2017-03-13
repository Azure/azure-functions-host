// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models.Swagger
{
    [JsonObject]
    public class HttpOperationParameterInfo
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "in")]
        public string Location { get; } = "path";

        [JsonProperty(PropertyName = "required")]
        public bool Required { get; } = true;

        [JsonProperty(PropertyName = "type")]
        public string DataType { get; set; }
    }
}