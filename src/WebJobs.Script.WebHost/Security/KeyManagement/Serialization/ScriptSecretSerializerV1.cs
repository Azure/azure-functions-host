// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal sealed class ScriptSecretSerializerV1 : IScriptSecretSerializer
    {
        public string SerializeSecrets<T>(T secrets) where T : ScriptSecrets => JsonConvert.SerializeObject(secrets, Formatting.Indented);

        public T DeserializeSecrets<T>(JObject secrets) where T : ScriptSecrets => secrets.ToObject<T>();

        public bool CanSerialize(JObject functionSecrets, Type type)
        {
            if (type == typeof(HostSecrets))
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["masterKey"]?.Type == JTokenType.Object &&
                    functionSecrets["functionKeys"]?.Type == JTokenType.Array;
            }
            else if (type == typeof(FunctionSecrets))
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["keys"]?.Type == JTokenType.Array;
            }

            return false;
        }
    }
}
