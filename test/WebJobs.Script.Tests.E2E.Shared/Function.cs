// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace WebJobs.Script.Tests.EndToEnd.Shared
{
    public class Function
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("config")]
        public Config Configuration { get; set; }

        public class Config
        {
            [JsonProperty("bindings")]
            public Binding[] Bindings { get; set; }

            [JsonProperty("disabled")]
            public bool Disabled { get; set; }
        }

        public class Binding
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("direction")]
            public string Direction { get; set; }
        }
    }
}
