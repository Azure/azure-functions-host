// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace WebJobs.Script.WebHost
{
    public class HostSecrets
    {
        /// <summary>
        /// Gets or sets the host master (admin) key value. These keys allow invocation of
        /// any function, and also permit access to additional admin operations.
        /// Can contain either a single key value, or multiple comma separated values.
        /// </summary>
        [JsonProperty(PropertyName = "masterKey")]
        public string MasterKey { get; set; }

        /// <summary>
        /// Gets or sets the host level function key value. These keys allow invocation of
        /// any function.
        /// Can contain either a single key value, or multiple comma separated values.
        /// </summary>
        [JsonProperty(PropertyName = "functionKey")]
        public string FunctionKey { get; set; }
    }
}