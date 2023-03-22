// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.Web.DataProtection;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal static class SecretsUtility
    {
        public static string GetNonDecryptableName(string secretsPath)
        {
            string timeStamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH.mm.ss.ffffff");
            if (secretsPath.EndsWith(".json"))
            {
                secretsPath = secretsPath.Substring(0, secretsPath.Length - 5);
            }
            return secretsPath + $".{ScriptConstants.Snapshot}.{timeStamp}.json";
        }

        public static bool TryGetEncryptionKey(out string key, IEnvironment environment = null)
        {
            environment = environment ?? SystemEnvironment.Instance;

            if (environment.IsKubernetesManagedHosting())
            {
                key = environment.GetEnvironmentVariable(EnvironmentSettingNames.PodEncryptionKey);
                if (!string.IsNullOrEmpty(key))
                {
                    return true;
                }
            }

            // Use WebSiteAuthEncryptionKey if available else fall back to ContainerEncryptionKey.
            // Until the container is specialized to a specific site WebSiteAuthEncryptionKey will not be available.
            if (TryGetEncryptionKey(environment, EnvironmentSettingNames.WebSiteAuthEncryptionKey, out key) ||
                TryGetEncryptionKey(environment, EnvironmentSettingNames.ContainerEncryptionKey, out key))
            {
                return true;
            }

            // Fall back to using DataProtection APIs to get the key
            key = Util.GetDefaultKeyValue();
            if (!string.IsNullOrEmpty(key))
            {
                return true;
            }

            return false;
        }

        public static string GetEncryptionKeyValue(IEnvironment environment = null)
        {
            if (TryGetEncryptionKey(out string key, environment))
            {
                return key;
            }
            else
            {
                throw new InvalidOperationException($"No encryption key defined in the environment.");
            }
        }

        public static byte[] GetEncryptionKey(IEnvironment environment = null)
        {
            string key = GetEncryptionKeyValue(environment);
            return key.ToKeyBytes();
        }

        public static byte[] ToKeyBytes(this string hexOrBase64)
        {
            // only support 32 bytes (256 bits) key length
            if (hexOrBase64.Length == 64)
            {
                return Enumerable.Range(0, hexOrBase64.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(hexOrBase64.Substring(x, 2), 16))
                    .ToArray();
            }

            return Convert.FromBase64String(hexOrBase64);
        }

        private static bool TryGetEncryptionKey(IEnvironment environment, string keyName, out string encryptionKey)
        {
            encryptionKey = environment.GetEnvironmentVariable(keyName);
            return !string.IsNullOrEmpty(encryptionKey);
        }
    }
}