// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security
{
    public static class SimpleWebTokenHelper
    {
        /// <summary>
        /// A SWT or a Simple Web Token is a token that's made of key=value pairs separated
        /// by &. We only specify expiration in ticks from now (exp={ticks})
        /// The SWT is then returned as an encrypted string
        /// </summary>
        /// <param name="validUntil">Datetime for when the token should expire</param>
        /// <returns>a SWT signed by this app</returns>
        public static string CreateToken(DateTime validUntil) => Encrypt($"exp={validUntil.Ticks}");

        internal static string Encrypt(string value, byte[] key = null)
        {
            key = key ?? GetWebSiteAuthEncryptionKey();

            using (var aes = new AesManaged { Key = key })
            {
                // IV is always generated for the key every time
                aes.GenerateIV();
                var input = Encoding.UTF8.GetBytes(value);
                var iv = Convert.ToBase64String(aes.IV);

                using (var encrypter = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var cipherStream = new MemoryStream())
                {
                    using (var cryptoStream = new CryptoStream(cipherStream, encrypter, CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cryptoStream))
                    {
                        binaryWriter.Write(input);
                        cryptoStream.FlushFinalBlock();
                    }

                    // return {iv}.{swt}.{sha236(key)}
                    return string.Format("{0}.{1}.{2}", iv, Convert.ToBase64String(cipherStream.ToArray()), GetSHA256Base64String(aes.Key));
                }
            }
        }

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

        public static bool TryValidateToken(string token, ISystemClock systemClock)
        {
            try
            {
                return ValidateToken(token, systemClock);
            }
            catch
            {
                return false;
            }
        }

        public static bool ValidateToken(string token, ISystemClock systemClock)
        {
            var data = Decrypt(GetWebSiteAuthEncryptionKey(), token);

            var parsedToken = data
                // token = key1=value1;key2=value2
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                 // ["key1=value1", "key2=value2"]
                 .Select(v => v.Split('=', StringSplitOptions.RemoveEmptyEntries))
                 // [["key1", "value1"], ["key2", "value2"]]
                 .ToDictionary(k => k[0], v => v[1]);

            return parsedToken.ContainsKey("exp") && systemClock.UtcNow.UtcDateTime < new DateTime(long.Parse(parsedToken["exp"]));
        }

        private static string GetSHA256Base64String(byte[] key)
        {
            using (var sha256 = new SHA256Managed())
            {
                return Convert.ToBase64String(sha256.ComputeHash(key));
            }
        }

        private static byte[] GetWebSiteAuthEncryptionKey()
        {
            var hexOrBase64 = Environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
            if (string.IsNullOrEmpty(hexOrBase64))
            {
                throw new InvalidOperationException("No WEBSITE_AUTH_ENCRYPTION_KEY defined in the environment");
            }

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
