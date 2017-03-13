// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models.Swagger
{
    [JsonObject]
    public class SwaggerDocument
    {
        public SwaggerDocument()
        {
            SecurityDefinitions = JObject.Parse("{\"apikeyQuery\":{\"type\":\"apiKey\",\"name\":\"code\",\"in\":\"query\"}}");
            Schemes = new Collection<string> { "https", "http" };
        }

        [JsonProperty(PropertyName = "swagger")]
        public string SwaggerVersion { get; } = "2.0";

        [JsonProperty(PropertyName = "info")]
        public SwaggerInfo SwaggerInfo { get; } = new SwaggerInfo();

        [JsonProperty(PropertyName = "host")]
        public string Host { get; set; }

        [JsonProperty(PropertyName = "basePath")]
        public string BasePath { get; } = "/";

        [JsonProperty(PropertyName = "schemes")]
        public Collection<string> Schemes { get; }

        [JsonProperty(PropertyName = "paths")]
        public Dictionary<string, Dictionary<string, HttpOperationInfo>> ApiEndpoints { get; set; }

        [JsonProperty(PropertyName = "definitions")]
        public JObject Definitions { get; } = new JObject();

        [JsonProperty(PropertyName = "securityDefinitions")]
        public JObject SecurityDefinitions { get; }
    }
}