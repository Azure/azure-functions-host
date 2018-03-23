// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class EncryptedHostAssignmentContext
    {
        [JsonProperty("encryptedContext")]
        public string EncryptedContext { get; set; }

        public HostAssignmentContext Decrypt(string key)
        {
            var encryptionKey = Convert.FromBase64String(key);
            var decrypted = SimpleWebTokenHelper.Decrypt(encryptionKey, EncryptedContext);
            return JsonConvert.DeserializeObject<HostAssignmentContext>(decrypted);
        }
    }
}
