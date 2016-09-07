// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal sealed class ScriptSecretSerializerV1 : IScriptSecretSerializer
    {
        public FunctionSecrets DeserializeFunctionSecrets(JObject secrets) => secrets.ToObject<FunctionSecrets>();

        public HostSecrets DeserializeHostSecrets(JObject secrets) => secrets.ToObject<HostSecrets>();

        public string SerializeHostSecrets(HostSecrets secrets) => JsonConvert.SerializeObject(secrets, Formatting.Indented);

        public string SerializeFunctionSecrets(FunctionSecrets secrets) => JsonConvert.SerializeObject(secrets, Formatting.Indented);

        public bool CanSerialize(JObject functionSecrets, SecretsType type)
        {
            if (type == SecretsType.Host)
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["masterKey"]?.Type == JTokenType.Object &&
                    functionSecrets["functionKeys"]?.Type == JTokenType.Array;
            }
            else if (type == SecretsType.Function)
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["keys"]?.Type == JTokenType.Array;
            }

            return false;
        }
    }
}
