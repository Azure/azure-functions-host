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
    internal class ScriptSecretSerializerV0 : IScriptSecretSerializer
    {
        private const string MasterKeyPropertyName = "masterKey";
        private const string HostFunctionKeyPropertyName = "functionKey";
        private const string FunctionKeyPropertyName = "key";

        public string SerializeSecrets<T>(T secrets) where T : ScriptSecrets
        {
            if (secrets is FunctionSecrets)
            {
                return SerializeFunctionSecrets(secrets as FunctionSecrets);
            }
            else if (secrets is HostSecrets)
            {
                return SerializeHostSecrets(secrets as HostSecrets);
            }

            return null;
        }

        public T DeserializeSecrets<T>(JObject secrets) where T : ScriptSecrets
        {
            var secretsType = typeof(T);
            if (typeof(FunctionSecrets).IsAssignableFrom(secretsType))
            {
                return DeserializeFunctionSecrets(secrets) as T;
            }
            else if (typeof(HostSecrets).IsAssignableFrom(secretsType))
            {
                return DeserializeHostSecrets(secrets) as T;
            }

            return default(T);
        }

        private static FunctionSecrets DeserializeFunctionSecrets(JObject secrets)
        {
            string key = secrets.Value<string>(FunctionKeyPropertyName);
            IList<Key> keys = CreateFunctionKeysCollection(key);

            return new FunctionSecrets(keys);
        }

        private static HostSecrets DeserializeHostSecrets(JObject secrets)
        {
            string masterSecret = secrets.Value<string>(MasterKeyPropertyName);
            string functionSecret = secrets.Value<string>(HostFunctionKeyPropertyName);

            return new HostSecrets
            {
                MasterKey = CreateKeyFromSecret(masterSecret, ScriptConstants.DefaultMasterKeyName),
                FunctionKeys = CreateFunctionKeysCollection(functionSecret)
            };
        }

        private static string SerializeFunctionSecrets(FunctionSecrets secrets)
        {
            // Output:
            //  { "key" : "keyvalue" }

            var functionSecrets = new JObject
            {
                [FunctionKeyPropertyName] = GetFunctionKeyFromCollection(secrets?.Keys)
            };

            return functionSecrets.ToString();
        }

        private static string SerializeHostSecrets(HostSecrets secrets)
        {
            // Output:
            //  {
            //    "masterKey" : "masterkeyvalue",
            //    "functionKey" : "functionkeyvalue"
            //  }

            var hostSecrets = new JObject
            {
                [MasterKeyPropertyName] = secrets.MasterKey.Value,
                [HostFunctionKeyPropertyName] = GetFunctionKeyFromCollection(secrets.FunctionKeys)
            };

            return hostSecrets.ToString();
        }

        private static IList<Key> CreateFunctionKeysCollection(string functionKey)
        {
            var keys = new List<Key>();

            if (functionKey != null)
            {
                keys.Add(CreateKeyFromSecret(functionKey, ScriptConstants.DefaultFunctionKeyName));
            }

            return keys;
        }

        private static string GetFunctionKeyFromCollection(IList<Key> keys)
        {
            Key key = keys?.FirstOrDefault();

            return keys?.FirstOrDefault(s => string.Equals(s.Name, ScriptConstants.DefaultFunctionKeyName))?.Value
                ?? key?.Value;
        }

        private static Key CreateKeyFromSecret(string secret, string name = "") => new Key(name, secret);

        public bool CanSerialize(JObject functionSecrets, Type type)
        {
            if (type == typeof(HostSecrets))
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    functionSecrets["masterKey"]?.Type == JTokenType.String;
            }
            else if (type == typeof(FunctionSecrets))
            {
                return functionSecrets != null &&
                    functionSecrets.Type == JTokenType.Object &&
                    (functionSecrets["key"]?.Type == JTokenType.String || functionSecrets["key"]?.Type == JTokenType.Null);
            }

            return false;
        }
    }
}
