// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class FunctionSecrets : ScriptSecrets
    {
        public FunctionSecrets()
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

        public override ScriptSecrets Refresh(IKeyValueConverterFactory factory)
        {
            var keys = Keys.Select(k => factory.GetValueWriter(k).WriteValue(k)).ToList();

            return new FunctionSecrets(keys);
        }
    }
}
