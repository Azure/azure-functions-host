// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.Compression;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class UnZipHandler : IUnZipHandler
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger<UnZipHandler> _logger;

        public UnZipHandler(IMetricsLogger metricsLogger, ILogger<UnZipHandler> logger)
        {
            _metricsLogger = metricsLogger;
            _logger = logger;
        }

        public void UnzipPackage(string filePath, string scriptPath)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationZipExtract))
            {
                _logger.LogDebug($"Extracting files to '{scriptPath}'");
                ZipFile.ExtractToDirectory(filePath, scriptPath, overwriteFiles: true);
                _logger.LogDebug("Zip extraction complete");
            }
        }
    }
}