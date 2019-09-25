// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class EncryptedHostAssignmentContext
    {
        [JsonProperty("encryptedContext")]
        public string EncryptedContext { get; set; }

        [JsonProperty("isWarmup")]
        public bool IsWarmup { get; set; }

        public static EncryptedHostAssignmentContext Create(HostAssignmentContext context, string key)
        {
            string json = JsonConvert.SerializeObject(context);
            var encryptionKey = Convert.FromBase64String(key);
            string encrypted = SimpleWebTokenHelper.Encrypt(json, encryptionKey);

            return new EncryptedHostAssignmentContext { EncryptedContext = encrypted };
        }

        public HostAssignmentContext Decrypt(string key)
        {
            var decrypted = SimpleWebTokenHelper.Decrypt(key.ToKeyBytes(), EncryptedContext);
            return JsonConvert.DeserializeObject<HostAssignmentContext>(decrypted);
        }
    }
}
