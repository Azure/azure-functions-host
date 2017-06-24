// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models.Swagger
{
    [JsonObject]
    public class SwaggerInfo
    {
        [JsonProperty(PropertyName = "title")]
        public string FunctionAppName { get; set; }

        [JsonProperty(PropertyName = "version")]
        public string Version { get; } = "1.0.0";
    }
}