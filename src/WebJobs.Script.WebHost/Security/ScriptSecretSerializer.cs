// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                // This is temporarily behind a feature flag. Once other clients are able to work with the new version, this should be removed.
                if (string.Equals(Environment.GetEnvironmentVariable("AzureWebJobsEnableMultiKey"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return _secretFormatters.Last();
                }

                return _secretFormatters.First();
            }
        }

        public static string SerializeFunctionSecrets(FunctionSecrets secrets) => DefaultSerializer.SerializeFunctionSecrets(secrets);

        public static string SerializeHostSecrets(HostSecrets secrets) => DefaultSerializer.SerializeHostSecrets(secrets);

        public static FunctionSecrets DeserializeFunctionSecrets(string secretsJson) => ResolveSerializerAndRun(secretsJson, SecretsType.Function, (s, o) => s.DeserializeFunctionSecrets(o));

        public static HostSecrets DeserializeHostSecrets(string secretsJson) => ResolveSerializerAndRun(secretsJson, SecretsType.Host, (s, o) => s.DeserializeHostSecrets(o));

        private static TResult ResolveSerializerAndRun<TResult>(string secretsJson, SecretsType type, Func<IScriptSecretSerializer, JObject, TResult> func)
        {
            JObject secrets = JObject.Parse(secretsJson);

            IScriptSecretSerializer serializer = GetSerializer(secrets, type);

            return func(serializer, secrets);
        }

        private static IScriptSecretSerializer GetSerializer(JObject secrets, SecretsType type)
        {
            IScriptSecretSerializer serializer = _secretFormatters.FirstOrDefault(s => s.CanSerialize(secrets, type));

            if (serializer == null)
            {
                throw new FormatException("Invalid secrets file format.");
            }

            return serializer;
        }
    }
}
