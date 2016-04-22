// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
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

        [JsonProperty(PropertyName = "keys")]
        public Dictionary<string, string> Keys { get; set; }

        public string GetKeyValue(string keyId)
        {
            string key = null;
            if (keyId != null && Keys != null && 
                Keys.TryGetValue(keyId, out key))
            {
                return key;
            }
            else
            {
                return Key;
            }
        }
    }
}