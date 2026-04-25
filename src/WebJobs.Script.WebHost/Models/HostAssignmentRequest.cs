// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public sealed class HostAssignmentRequest
    {
        [JsonProperty("encryptedContext", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string EncryptedContext { get; set; }

        [JsonProperty("assignmentContext", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public HostAssignmentContext AssignmentContext { get; set; }

        /// <summary>
        /// Legion Goal 3 runtime container assignment: structured platform metadata.
        /// When present, <see cref="Environment"/> must also be set.
        /// Mutually exclusive with <see cref="EncryptedContext"/> and <see cref="AssignmentContext"/>.
        /// </summary>
        [JsonProperty("apiServerAssignmentRequest", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public RuntimeApiServerAssignmentRequest ApiServerAssignmentRequest { get; set; }

        /// <summary>
        /// Legion Goal 3 runtime container assignment: environment variables.
        /// Only used when <see cref="ApiServerAssignmentRequest"/> is present.
        /// </summary>
        [JsonProperty("environment", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, string> Environment { get; set; }
    }
}
