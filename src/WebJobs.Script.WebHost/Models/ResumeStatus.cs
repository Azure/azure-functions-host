// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ResumeStatus
    {
        [JsonProperty(PropertyName = "hostStatus", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HostStatus HostStatus { get; set; }
    }
}
