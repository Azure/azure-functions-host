// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class EncryptedHostAssignmentContext
    {
        [JsonProperty("encryptedContext")]
        public string EncryptedContext { get; set; }

        [JsonProperty("isWarmup")]
        public bool IsWarmup { get; set; }
    }
}
