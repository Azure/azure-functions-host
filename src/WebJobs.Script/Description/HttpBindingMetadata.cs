// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class HttpBindingMetadata : BindingMetadata
    {
        public HttpBindingMetadata()
        {
            AuthLevel = AuthorizationLevel.Function;
        }

        public string Route { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public AuthorizationLevel AuthLevel { get; set; }

        public string WebHookType { get; set; }
    }
}
