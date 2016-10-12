// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class HttpTriggerBindingMetadata : BindingMetadata
    {
        public HttpTriggerBindingMetadata()
        {
            AuthLevel = AuthorizationLevel.Function;
        }

        /// <summary>
        /// Gets or sets the route template for the function. Can include
        /// route parameters using WebApi supported syntax. If not specified,
        /// will default to the function name.
        /// See: https://www.asp.net/web-api/overview/web-api-routing-and-actions/attribute-routing-in-web-api-2#constraints
        /// </summary>
        public string Route { get; set; }

        [JsonProperty(ItemConverterType = typeof(HttpMethodJsonConverter))]
        public Collection<HttpMethod> Methods { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public AuthorizationLevel AuthLevel { get; set; }

        public string WebHookType { get; set; }
    }
}
