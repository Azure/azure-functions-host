// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class ScriptSecretSerializer
    {
        private static IScriptSecretSerializer _defaultSerializer = new ScriptSecretSerializerV1();
        private static List<IScriptSecretSerializer> _secretFormatters = new List<IScriptSecretSerializer>
        {
            new ScriptSecretSerializerV0(),
            _defaultSerializer
        };

        internal static IScriptSecretSerializer DefaultSerializer => _defaultSerializer;

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
