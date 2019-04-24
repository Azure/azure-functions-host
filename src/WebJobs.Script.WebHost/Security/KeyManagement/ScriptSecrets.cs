// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public abstract class ScriptSecrets
    {
        protected ScriptSecrets()
        {
            InstanceId = ScriptSettingsManager.Instance.AzureWebsiteInstanceId;
            Source = ScriptConstants.Runtime;
        }

        [JsonIgnore]
        public abstract bool HasStaleKeys { get; }

        [JsonIgnore]
        public abstract ScriptSecretsType SecretsType { get; }

        [JsonProperty(PropertyName = "hostName")]
        public string HostName { get; set; }

        [JsonProperty(PropertyName = "instanceId")]
        public string InstanceId { get; set; }

        [JsonProperty(PropertyName = "source")]
        public string Source { get; set; }

        [JsonProperty(PropertyName = "decryptionKeyId")]
        public string DecryptionKeyId { get; set; }

        protected abstract ICollection<Key> GetKeys(string keyScope);

        public abstract ScriptSecrets Refresh(IKeyValueConverterFactory factory);

        public abstract IEnumerator<Key> GetEnumerator();

        public virtual void AddKey(Key item, string keyScope) => GetKeys(keyScope)?.Add(item);

        public virtual bool RemoveKey(Key item, string keyScope) => GetKeys(keyScope)?.Remove(item) ?? false;

        public virtual Key GetFunctionKey(string name, string keyScope) => GetKeys(keyScope)?.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
