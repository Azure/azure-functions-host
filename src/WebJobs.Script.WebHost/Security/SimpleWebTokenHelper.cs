// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security
{
    internal static class SimpleWebTokenHelper
    {
        public static string Decrypt(byte[] encryptionKey, string value)
        {
            var parts = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 && parts.Length != 3)
            {
                throw new InvalidOperationException("Malformed token.");
            }

            var iv = Convert.FromBase64String(parts[0]);
            var data = Convert.FromBase64String(parts[1]);
            var base64KeyHash = parts.Length == 3 ? parts[2] : null;

            if (!string.IsNullOrEmpty(base64KeyHash) && !string.Equals(GetSHA256Base64String(encryptionKey), base64KeyHash))
            {
                throw new InvalidOperationException(string.Format("Key with hash {0} does not exist.", base64KeyHash));
            }

            using (var aes = new AesManaged { Key = encryptionKey })
            {
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv), CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cs))
                    {
                        binaryWriter.Write(data, 0, data.Length);
                    }

                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        public static bool TryValidateToken(string token, DateTime dateTime)
        {
            try
            {
                return ValidateToken(token, dateTime);
            }
            catch
            {
                return false;
            }
        }

        public static bool ValidateToken(string token, DateTime dateTime)
        {
            // Use WebSiteAuthEncryptionKey if available.
            byte[] key = GetEncryptionKey(EnvironmentSettingNames.WebsiteAuthEncryptionKey);

            var data = Decrypt(key, token);

            var parsedToken = data
                // token = key1=value1;key2=value2
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                 // ["key1=value1", "key2=value2"]
                 .Select(v => v.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                 // [["key1", "value1"], ["key2", "value2"]]
                 .ToDictionary(k => k[0], v => v[1]);

            return parsedToken.ContainsKey("exp") && dateTime < new DateTime(long.Parse(parsedToken["exp"]));
        }

        private static string GetSHA256Base64String(byte[] key)
        {
            using (var sha256 = new SHA256Managed())
            {
                return Convert.ToBase64String(sha256.ComputeHash(key));
            }
        }

        internal static byte[] GetEncryptionKey(string keyName)
        {
            var hexOrBase64 = Environment.GetEnvironmentVariable(keyName);
            if (string.IsNullOrEmpty(hexOrBase64))
            {
                throw new InvalidOperationException($"No {keyName} defined in the environment");
            }

            return hexOrBase64.ToKeyBytes();
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
