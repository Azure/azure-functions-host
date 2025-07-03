// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security
{
    public static class EncryptionHelper
    {
        internal static string Encrypt(string value, byte[] key = null, IEnvironment environment = null, bool includesSignature = false)
        {
            key = key ?? SecretsUtility.GetEncryptionKey(environment);

            using (var aes = Aes.Create())
            {
                aes.Key = key;

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

                    if (includesSignature)
                    {
                        return $"{Convert.ToBase64String(aes.IV)}.{Convert.ToBase64String(cipherStream.ToArray())}.{GetSHA256Base64String(aes.Key)}.{Convert.ToBase64String(ComputeHMACSHA256(aes.Key, input))}";
                    }
                    else
                    {
                        // return {iv}.{content}.{sha236(key)}
                        return string.Format("{0}.{1}.{2}", iv, Convert.ToBase64String(cipherStream.ToArray()), GetSHA256Base64String(aes.Key));
                    }
                }
            }
        }

        public static string Decrypt(byte[] encryptionKey, string value)
        {
            var parts = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 && parts.Length != 3 && parts.Length != 4)
            {
                throw new InvalidOperationException("Malformed token.");
            }

            var iv = Convert.FromBase64String(parts[0]);
            var data = Convert.FromBase64String(parts[1]);
            var base64KeyHash = parts.Length == 3 ? parts[2] : null;
            var signature = parts.Length == 4 ? Convert.FromBase64String(parts[3]) : null;

            if (!string.IsNullOrEmpty(base64KeyHash) && !string.Equals(GetSHA256Base64String(encryptionKey), base64KeyHash))
            {
                throw new InvalidOperationException(string.Format("Key with hash {0} does not exist.", base64KeyHash));
            }

            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey;

                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv), CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cs))
                    {
                        binaryWriter.Write(data, 0, data.Length);
                    }

                    var input = ms.ToArray();
                    if (signature != null && !signature.SequenceEqual(ComputeHMACSHA256(encryptionKey, input)))
                    {
                        throw new InvalidOperationException("Signature mismatches!");
                    }

                    return Encoding.UTF8.GetString(input);
                }
            }
        }

        public static string Decrypt(string value, IEnvironment environment = null)
        {
            byte[] key = SecretsUtility.GetEncryptionKey(environment);
            return Decrypt(key, value);
        }

        private static string GetSHA256Base64String(byte[] key)
        {
            using (var sha256 = SHA256.Create())
            {
                return Convert.ToBase64String(sha256.ComputeHash(key));
            }
        }

        private static byte[] ComputeHMACSHA256(byte[] key, byte[] input)
        {
            using (var hmacSha256 = new HMACSHA256(key))
            {
                return hmacSha256.ComputeHash(input);
            }
        }
    }
}
