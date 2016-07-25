// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Web.UI.WebControls.Expressions;
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

        public string Route { get; set; }

        [JsonProperty(ItemConverterType = typeof(HttpMethodJsonConverter))]
        public Collection<HttpMethod> Methods { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public AuthorizationLevel AuthLevel { get; set; }

        public string WebHookType { get; set; }
    }
}
