// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    internal static class CgroupMemoryUsageHelper
    {
        private const string CgroupPathV1 = "/sys/fs/cgroup/memory/memory.usage_in_bytes";
        private const string CgroupPathV2 = "/sys/fs/cgroup/memory.current";

        /// <summary>
        /// Retrieves the memory usage of the control group in bytes, supporting both cgroup v1 and v2.
        /// </summary>
        /// <param name="logger">An <see cref="ILogger{TCategoryName}"/> instance for logging.</param>
        /// <returns>The memory usage in bytes if available; otherwise, 0.</returns>
        internal static long GetMemoryUsageInBytes(ILogger logger)
        {
            try
            {
                if (TryReadMemoryUsage(CgroupPathV2, out var usageInBytes) || TryReadMemoryUsage(CgroupPathV1, out usageInBytes))
                {
                    return usageInBytes;
                }

                logger.LogWarning("Memory usage not available from either control group v1 or v2.");
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reading control group resource usage.");
                return 0;
            }
        }

        private static bool TryReadMemoryUsage(string path, out long memoryUsageInBytes)
        {
            memoryUsageInBytes = 0;

            if (!File.Exists(path))
            {
                return false;
            }

            return long.TryParse(File.ReadAllText(path), out memoryUsageInBytes);
        }
    }
}
