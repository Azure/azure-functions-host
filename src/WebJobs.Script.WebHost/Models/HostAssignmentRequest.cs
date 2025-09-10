// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostAssignmentRequest
    {
        [JsonProperty("encryptedContext")]
        public string EncryptedContext { get; set; }

        [JsonProperty("assignmentContext")]
        public HostAssignmentContext AssignmentContext { get; set; }
    }
}
