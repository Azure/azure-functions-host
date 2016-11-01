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
        protected abstract ICollection<Key> InnerFunctionKeys { get; }

        [JsonIgnore]
        public abstract ScriptSecretsType SecretsType { get; }

        public abstract ScriptSecrets Refresh(IKeyValueConverterFactory factory);

        public abstract IEnumerator<Key> GetEnumerator();

        public virtual void AddKey(Key item) => InnerFunctionKeys?.Add(item);

        public virtual bool RemoveKey(Key item) => InnerFunctionKeys?.Remove(item) ?? false;

        public virtual Key GetFunctionKey(string name) => InnerFunctionKeys?.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
