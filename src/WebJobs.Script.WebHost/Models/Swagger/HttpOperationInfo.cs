// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models.Swagger
{
    [JsonObject]
    public class HttpOperationInfo
    {
        public HttpOperationInfo(string endpoint, string httpOperation)
        {
            Security = JArray.Parse("[{\"apikeyQuery\":[]}]");
            Responses = JObject.Parse("{\"200\":{\"description\":\"Success operation\"}}");
            OperationId = $"{endpoint}/{httpOperation}";
        }

        [JsonProperty(PropertyName = "operationId")]
        public string OperationId { get; set; }

        [JsonProperty(PropertyName = "produces")]
        public Collection<string> Produces { get; } = new Collection<string> { };

        [JsonProperty(PropertyName = "consumes")]
        public Collection<string> Consumes { get; } = new Collection<string> { };

        [JsonProperty(PropertyName = "parameters")]
        public ICollection<HttpOperationParameterInfo> InputParameters { get; set; }

        [JsonProperty(PropertyName = "description")]
        public string Description { get; } = "Replace with Operation Object #http://swagger.io/specification/#operationObject";

        [JsonProperty(PropertyName = "responses")]
        public JObject Responses { get; }

        [JsonProperty(PropertyName = "security")]
        public JArray Security { get; }
    }
}