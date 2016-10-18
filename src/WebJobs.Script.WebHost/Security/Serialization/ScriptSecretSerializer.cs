// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class ScriptSecretSerializer
    {
        private static List<IScriptSecretSerializer> _secretFormatters = new List<IScriptSecretSerializer>
        {
            new ScriptSecretSerializerV0(),
            new ScriptSecretSerializerV1()
        };

        internal static IScriptSecretSerializer DefaultSerializer
        {
            get
            {
                // This is temporarily behind a feature flag. Once other clients are able to
                // work with the new version, this should be removed.
                if (FeatureFlags.IsEnabled("MultiKey"))
                {
                    return _secretFormatters.Last();
                }

                return _secretFormatters.First();
            }
        }

        public static ScriptSecrets DeserializeSecrets(ScriptSecretsType secretsType, string secretsJson)
        {
            return secretsType == ScriptSecretsType.Function
                ? DeserializeSecrets<FunctionSecrets>(secretsJson) as ScriptSecrets
                : DeserializeSecrets<HostSecrets>(secretsJson) as ScriptSecrets;
        }

        public static string SerializeSecrets<T>(T secrets) where T : ScriptSecrets => DefaultSerializer.SerializeSecrets(secrets);

        public static T DeserializeSecrets<T>(string secretsJson) where T : ScriptSecrets => ResolveSerializerAndRun(secretsJson, typeof(T), (s, o) => s.DeserializeSecrets<T>(o));

        private static TResult ResolveSerializerAndRun<TResult>(string secretsJson, Type secretsType, Func<IScriptSecretSerializer, JObject, TResult> func)
        {
            JObject secrets = JObject.Parse(secretsJson);

            IScriptSecretSerializer serializer = GetSerializer(secrets, secretsType);

            return func(serializer, secrets);
        }

        private static IScriptSecretSerializer GetSerializer(JObject secrets, Type secretType)
        {
            IScriptSecretSerializer serializer = _secretFormatters.FirstOrDefault(s => s.CanSerialize(secrets, secretType));

            if (serializer == null)
            {
                throw new FormatException("Invalid secrets file format.");
            }

            return serializer;
        }
    }
}
