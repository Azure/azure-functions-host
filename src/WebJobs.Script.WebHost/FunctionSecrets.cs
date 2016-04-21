// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace WebJobs.Script.WebHost
{
    public class FunctionSecrets
    {
        /// <summary>
        /// Gets or sets the function specific key value. These keys only allow invocation of
        /// the single function they apply to.
        /// Can contain either a single key value, or multiple comma separated values.
        /// </summary>
        [JsonProperty(PropertyName = "key")]
        public string Key { get; set; }
    }
}