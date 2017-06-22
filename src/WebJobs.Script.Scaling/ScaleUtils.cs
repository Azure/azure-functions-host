// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
            var bytes = BitConverter.GetBytes(expiredUtc.Ticks);

            // TODO: FACAVAL: Get machine key (use same approach as data protection)
            // var encrypted = MachineKey.Protect(bytes, Purpose);
            var encrypted = new byte[0];
            return Convert.ToBase64String(encrypted);
        }

        public static void ValidateToken(string token)
        {
            var encrypted = Convert.FromBase64String(token);

            // TODO: FACAVAL: Get machine key (use same approach as data protection)
            // var bytes = MachineKey.Unprotect(encrypted, Purpose);
            var bytes = new byte[0];
            var ticks = BitConverter.ToInt64(bytes, 0);
            var expiredUtc = new DateTime(ticks, DateTimeKind.Utc);

            // add 5 mins clock skew
            if (expiredUtc.AddMinutes(5) < DateTime.UtcNow)
            {
                throw new InvalidOperationException(string.Format("Token has expired at {0}", expiredUtc));
            }
        }
    }
}
