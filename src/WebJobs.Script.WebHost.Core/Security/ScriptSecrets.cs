// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public abstract class ScriptSecrets
    {
        protected ScriptSecrets()
        {
        }

        [JsonIgnore]
        public abstract bool HasStaleKeys { get; }

        [JsonIgnore]
        public abstract ScriptSecretsType SecretsType { get; }

        protected abstract ICollection<Key> GetKeys(string keyScope);

        public abstract ScriptSecrets Refresh(IKeyValueConverterFactory factory);

        public abstract IEnumerator<Key> GetEnumerator();

        public virtual void AddKey(Key item, string keyScope) => GetKeys(keyScope)?.Add(item);

        public virtual bool RemoveKey(Key item, string keyScope) => GetKeys(keyScope)?.Remove(item) ?? false;

        public virtual Key GetFunctionKey(string name, string keyScope) => GetKeys(keyScope)?.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
