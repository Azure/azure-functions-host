// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class DrainModeStatus
    {
        [JsonProperty("state")]
        [JsonConverter(typeof(StringEnumConverter))]
        public DrainModeState State { get; set; }

        [JsonProperty("outstandingInvocations")]
        public int OutstandingInvocations { get; set; }

        [JsonProperty("outstandingRetries")]
        public int OutstandingRetries { get; set; }
    }
}
