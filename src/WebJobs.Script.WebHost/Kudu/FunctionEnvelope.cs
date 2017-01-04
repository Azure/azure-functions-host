﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class FunctionEnvelope
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "function_app_id")]
        public string FunctionAppId { get; set; }

        [JsonProperty(PropertyName = "script_root_path_href")]
        public Uri ScriptRootPathHref { get; set; }

        [JsonProperty(PropertyName = "script_href")]
        public Uri ScriptHref { get; set; }

        [JsonProperty(PropertyName = "config_href")]
        public Uri ConfigHref { get; set; }

        [JsonProperty(PropertyName = "secrets_file_href")]
        public Uri SecretsFileHref { get; set; }

        [JsonProperty(PropertyName = "href")]
        public Uri Href { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [JsonProperty(PropertyName = "config")]
        public JObject Config { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [JsonProperty(PropertyName = "files")]
        public IDictionary<string, string> Files { get; set; }

        [JsonProperty(PropertyName = "test_data")]
        public string TestData { get; set; }
    }
}