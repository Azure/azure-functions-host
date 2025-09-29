// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public sealed class HostAssignmentRequest
    {
        [JsonProperty("encryptedContext", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string EncryptedContext { get; set; }

        [JsonProperty("assignmentContext", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HostAssignmentContext AssignmentContext { get; set; }
    }
}
