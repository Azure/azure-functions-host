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
    internal class ScriptSecretSerializerV0 : IScriptSecretSerializer
    {
        private const string MasterKeyPropertyName = "masterKey";
        private const string HostFunctionKeyPropertyName = "functionKey";
        private const string FunctionKeyPropertyName = "key";

        public FunctionSecrets DeserializeFunctionSecrets(JObject secrets)
        {
            string key = secrets.Value<string>(FunctionKeyPropertyName);

            return new FunctionSecrets(new List<Key> { CreateKeyFromSecret(key, SecretManager.DefaultFunctionKeyName) });
        }

        public HostSecrets DeserializeHostSecrets(JObject secrets)
        {
            string masterSecret = secrets.Value<string>(MasterKeyPropertyName);
            string functionSecret = secrets.Value<string>(HostFunctionKeyPropertyName);

            return new HostSecrets
            {
                MasterKey = CreateKeyFromSecret(masterSecret, SecretManager.DefaultMasterKeyName),
                FunctionKeys = new List<Key> { CreateKeyFromSecret(functionSecret, SecretManager.DefaultFunctionKeyName) }
            };
        }

        public string SerializeFunctionSecrets(FunctionSecrets secrets)
        {
            // Output:
            //  { "key" : "keyvalue" }

            var functionSecrets = new JObject
            {
                [FunctionKeyPropertyName] = secrets?.Keys.FirstOrDefault(s => string.IsNullOrEmpty(s.Name))?.Value
            };

            return functionSecrets.ToString();
        }

        public string SerializeHostSecrets(HostSecrets secrets)
        {
            // Output:
            //  { 
            //    "masterKey" : "masterkeyvalue",
            //    "functionKey" : "functionkeyvalue"
            //  }

            string functionKey = secrets.FunctionKeys
                    ?.FirstOrDefault(k => string.IsNullOrEmpty(k.Name))
                    ?.Value;

            var hostSecrets = new JObject
            {
                [MasterKeyPropertyName] = secrets.MasterKey.Value,
                [HostFunctionKeyPropertyName] = functionKey
            };

            return hostSecrets.ToString();
        }

        private static Key CreateKeyFromSecret(string secret, string name = "") => new Key(name, secret);

        public bool CanSerialize(JObject functionSecrets, SecretsType type)
        {
            if (type == SecretsType.Host)
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["masterKey"]?.Type == JTokenType.String;
            }
            else if (type == SecretsType.Function)
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["key"]?.Type == JTokenType.String;
            }

            return false;
        }
    }
}
