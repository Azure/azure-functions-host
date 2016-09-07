// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class HostSecrets
    {
        [JsonProperty(PropertyName = "masterKey")]
        public Key MasterKey { get; set; }

        [JsonProperty(PropertyName = "functionKeys")]
        public IList<Key> FunctionKeys { get; set; }

        [JsonProperty(PropertyName = "version")]
        private int Version
        {
            get { return 1; }
        }
    }
}