// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Management.Models
{
    public class FunctionMetadataResponse
    {
        /// <summary>
        /// Gets or sets the function name
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the script folder url
        /// </summary>
        [JsonProperty(PropertyName = "script_root_path_href")]
        public Uri ScriptRootPathHref { get; set; }

        /// <summary>
        /// Gets or sets script file url
        /// </summary>
        [JsonProperty(PropertyName = "script_href")]
        public Uri ScriptHref { get; set; }

        /// <summary>
        /// Gets or sets function config file url
        /// </summary>
        [JsonProperty(PropertyName = "config_href")]
        public Uri ConfigHref { get; set; }

        /// <summary>
        /// Gets or sets function test data url
        /// </summary>
        [JsonProperty(PropertyName = "test_data_href")]
        public Uri TestDataHref { get; set; }

        /// <summary>
        /// Gets or sets current function self link
        /// </summary>
        [JsonProperty(PropertyName = "href")]
        public Uri Href { get; set; }

        /// <summary>
        /// Gets or sets invoke url for the function, if one is supported (e.g. HTTP triggered functions)
        /// </summary>
        [JsonProperty(PropertyName = "invoke_url_template")]
        public Uri InvokeUrlTemplate { get; set; }

        [JsonProperty(PropertyName = "language")]
        public string Language { get; set; }

        /// <summary>
        /// Gets or sets function config json
        /// </summary>
        [JsonProperty(PropertyName = "config")]
        public JObject Config { get; set; }

        /// <summary>
        /// Gets or sets flat list of files and their content.
        /// The dictionary is fileName => fileContent
        /// </summary>
        [JsonProperty(PropertyName = "files")]
        public IDictionary<string, string> Files { get; set; }

        /// <summary>
        /// Gets or sets the test data string.
        /// This is only used for the UI and only supports string inputs.
        /// </summary>
        [JsonProperty(PropertyName = "test_data")]
        public string TestData { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the function is disabled
        /// </summary>
        [JsonProperty(PropertyName = "isDisabled")]
        public bool IsDisabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the function is direct.
        /// </summary>
        [JsonProperty(PropertyName = "isDirect")]
        public bool IsDirect { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the function is a proxy function.
        /// </summary>
        [JsonProperty(PropertyName = "isProxy")]
        public bool IsProxy { get; set; }
    }
}