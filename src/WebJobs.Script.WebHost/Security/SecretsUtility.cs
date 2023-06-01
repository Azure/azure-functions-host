﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.IdentityModel.Tokens;

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

        public static bool TryGetEncryptionKey(out string key)
        {
            if (TryGetEncryptionKey(EnvironmentSettingNames.WebsiteAuthEncryptionKey, out key))
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

        public static string GetEncryptionKeyValue()
        {
            if (TryGetEncryptionKey(out string key))
            {
                return key;
            }
            else
            {
                throw new InvalidOperationException($"No encryption key defined in the environment.");
            }
        }

        public static byte[] GetEncryptionKey()
        {
            string key = GetEncryptionKeyValue();
            return key.ToKeyBytes();
        }

        public static SymmetricSecurityKey[] GetTokenIssuerSigningKeys()
        {
            List<SymmetricSecurityKey> signingKeys = new List<SymmetricSecurityKey>();

            // first we want to use the DataProtection APIs to get the default key,
            // which will return any user specified AzureWebEncryptionKey with precedence
            // over the platform default key
            string defaultKey = Util.GetDefaultKeyValue();
            if (defaultKey != null)
            {
                signingKeys.Add(new SymmetricSecurityKey(defaultKey.ToKeyBytes()));
                signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(defaultKey)));
            }

            // next we want to ensure a key is also added for the platform default key
            // if it wasn't already added above
            if (SecretsUtility.TryGetEncryptionKey(out string key) && !string.Equals(key, defaultKey))
            {
                signingKeys.Add(new SymmetricSecurityKey(key.ToKeyBytes()));
            }

            return signingKeys.ToArray();
        }

        public static bool TryGetEncryptionKey(string keyName, out string encryptionKey)
        {
            encryptionKey = Environment.GetEnvironmentVariable(keyName);
            return !string.IsNullOrEmpty(encryptionKey);
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
    }
}