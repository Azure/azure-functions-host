// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpOutputBindingResponse
    {
        [JsonProperty(Required = Required.Always)]
        public string StatusCode { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string Status { get; set; }

        [JsonProperty(Required = Required.Always)]
        public object Body { get; set; }

        [JsonProperty(Required = Required.Always)]
        public IDictionary<string, object> Headers { get; set; }
    }
}
