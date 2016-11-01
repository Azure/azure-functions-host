// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class HostSecrets : ScriptSecrets
    {
        [JsonProperty(PropertyName = "masterKey")]
        public Key MasterKey { get; set; }

        [JsonProperty(PropertyName = "functionKeys")]
        public IList<Key> FunctionKeys { get; set; }

        [JsonIgnore]
        public override bool HasStaleKeys => (MasterKey?.IsStale ?? false) || (FunctionKeys?.Any(k => k.IsStale) ?? false);

        [JsonIgnore]
        protected override ICollection<Key> InnerFunctionKeys => FunctionKeys;

        [JsonIgnore]
        public override ScriptSecretsType SecretsType => ScriptSecretsType.Host;

        public override ScriptSecrets Refresh(IKeyValueConverterFactory factory)
        {
            var secrets = new HostSecrets
            {
                MasterKey = factory.WriteKey(MasterKey),
                FunctionKeys = FunctionKeys.Select(k => factory.WriteKey(k)).ToList()
            };

            return secrets;
        }

        public override IEnumerator<Key> GetEnumerator()
        {
            return FunctionKeys.GetEnumerator();
        }
    }
}