// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class FunctionSecrets : ScriptSecrets
    {
        public FunctionSecrets()
            : this(new List<Key>())
        {
        }

        public FunctionSecrets(IList<Key> keys)
        {
            Keys = keys;
        }

        [JsonProperty(PropertyName = "keys")]
        public IList<Key> Keys { get; set; }

        [JsonIgnore]
        public override bool HasStaleKeys => Keys?.Any(k => k.IsStale) ?? false;

        [JsonIgnore]
        public override ScriptSecretsType SecretsType => ScriptSecretsType.Function;

        protected override ICollection<Key> GetKeys(string keyScope) => Keys;

        public override ScriptSecrets Refresh(IKeyValueConverterFactory factory)
        {
            var keys = Keys.Select(k => factory.GetValueWriter(k).WriteValue(k)).ToList();

            return new FunctionSecrets(keys);
        }

        public override IEnumerator<Key> GetEnumerator()
        {
            return Keys.GetEnumerator();
        }
    }
}
