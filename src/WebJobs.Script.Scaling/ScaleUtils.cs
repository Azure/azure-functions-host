// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", Justification = "By design")]
    public static class ScaleUtils
    {
        public const string Purpose = "ScaleManager";

        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", Justification = "By design")]
        public static bool WorkerEquals(IWorkerInfo src, IWorkerInfo dst)
        {
            if (src == null && dst == null)
            {
                return true;
            }
            else if (src == null || dst == null)
            {
                return false;
            }

            return string.Equals(src.StampName, dst.StampName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(src.WorkerName, dst.WorkerName, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<IEnumerable<IWorkerInfo>> ListNonStale(this IWorkerTable table)
        {
            var workers = await table.List();
            return workers.Where(w => !w.IsStale);
        }

        public static async Task<IEnumerable<IWorkerInfo>> ListStale(this IWorkerTable table)
        {
            var workers = await table.List();
            return workers.Where(w => w.IsStale);
        }

        public static IEnumerable<IWorkerInfo> SortByRemovingOrder(this IEnumerable<IWorkerInfo> workers)
        {
            return workers.OrderBy(w =>
            {
                if (w.LoadFactor == int.MaxValue)
                {
                    return w.LoadFactor;
                }

                var offset = w.IsHomeStamp ? 100 : 0;
                return offset + w.LoadFactor;
            });
        }

        public static string GetSummary(this IEnumerable<IWorkerInfo> workers, string header)
        {
            var strb = new StringBuilder();
            strb.AppendFormat("{0} {1} workers", header, workers.Count());
            foreach (var worker in workers)
            {
                strb.AppendLine();
                strb.AppendFormat("{0}, loadfactor: {1}, stale: {2}, lastupdate: {3}", worker.ToDisplayString(), worker.LoadFactor, worker.IsStale, worker.LastModifiedTimeUtc);
            }

            return strb.ToString();
        }

        public static string ToDisplayString(this IWorkerInfo worker)
        {
            return string.Join(":", worker.StampName, worker.WorkerName);
        }

        public static string ToDisplayString(this IEnumerable<IWorkerInfo> workers)
        {
            return string.Join(",", workers.Select(w => w.ToDisplayString()));
        }

        public static string ToDisplayString(this IEnumerable<string> values)
        {
            return string.Join(",", values);
        }

        public static string GetToken(DateTime expiredUtc)
        {
            var swt = new StringBuilder();
            swt.AppendFormat("exp={0}", expiredUtc.Ticks);

            using (var aes = new AesManaged())
            {
                aes.Key = AppServiceSettings.RuntimeEncryptionKey;
                aes.GenerateIV();

                var input = Encoding.UTF8.GetBytes(swt.ToString());
                var iv = Convert.ToBase64String(aes.IV);
                using (var encrypter = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encrypter, CryptoStreamMode.Write))
                    using (var writer = new BinaryWriter(cs))
                    {
                        writer.Write(input);
                        cs.FlushFinalBlock();
                    }

                    return string.Format("{0}.{1}.{2}", iv, Convert.ToBase64String(ms.ToArray()), GetSHA256Base64String(aes.Key));
                }
            }
        }

        public static void ValidateToken(string token)
        {
            var parts = token.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 && parts.Length != 3)
            {
                throw new ArgumentException("Malform encrypted data.");
            }

            var iv = Convert.FromBase64String(parts[0]);
            var data = Convert.FromBase64String(parts[1]);
            var base64KeyHash = parts.Length == 3 ? parts[2] : null;

            var encryptionKey = AppServiceSettings.RuntimeEncryptionKey;
            if (!string.IsNullOrEmpty(base64KeyHash) && !string.Equals(GetSHA256Base64String(encryptionKey), base64KeyHash))
            {
                throw new InvalidOperationException(string.Format("Key with hash {0} does not exist.", base64KeyHash));
            }

            using (var aes = new AesManaged())
            {
                aes.Key = encryptionKey;

                using (var decrypter = aes.CreateDecryptor(aes.Key, iv))
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, decrypter, CryptoStreamMode.Write))
                    using (var writer = new BinaryWriter(cs))
                    {
                        // Decrypt Cipher Text from Message
                        writer.Write(data, 0, data.Length);
                    }

                    var swt = Encoding.UTF8.GetString(ms.ToArray());
                    var pair = swt.Split('&')
                                  .ToDictionary(p => p.Split('=')[0], p => p.Split('=')[1]);

                    var expiredUtc = new DateTime(long.Parse(pair["exp"]), DateTimeKind.Utc);

                    // add 5 mins clock skew
                    if (expiredUtc.AddMinutes(5) < DateTime.UtcNow)
                    {
                        throw new InvalidOperationException(string.Format("Token has expired at {0}", expiredUtc));
                    }
                }
            }
        }

        private static string GetSHA256Base64String(byte[] key)
        {
            using (var sha256 = new SHA256Managed())
            {
                return Convert.ToBase64String(sha256.ComputeHash(key));
            }
        }
    }
}
