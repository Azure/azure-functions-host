// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public class LegionMetricsFileManager
    {
        private readonly IFileSystem _fileSystem;
        private readonly int _maxFileCount;
        private readonly ILogger _logger;

        public LegionMetricsFileManager(string metricsFilePath, IFileSystem fileSystem, ILogger logger, int maxFileCount)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            MetricsFilePath = metricsFilePath;
            _maxFileCount = maxFileCount;
        }

        internal string MetricsFilePath { get; set;  }

        private bool PrepareDirectoryForFile()
        {
            if (string.IsNullOrEmpty(MetricsFilePath))
            {
                return false;
            }

            // ensure the directory exists
            _fileSystem.Directory.CreateDirectory(MetricsFilePath);

            var metricsDirectoryInfo = _fileSystem.DirectoryInfo.FromDirectoryName(MetricsFilePath);
            var files = metricsDirectoryInfo.GetFiles().OrderBy(p => p.CreationTime).ToList();

            // ensure we're under the max file count
            if (files.Count < _maxFileCount)
            {
                return true;
            }

            // we're at or over limit
            // delete enough files that we have space to write a new one
            int numToDelete = files.Count - _maxFileCount + 1;
            var filesToDelete = files.Take(numToDelete).ToArray();

            _logger.LogDebug($"Deleting {filesToDelete.Length} metrics file(s).");

            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    // best effort
                    _logger.LogError(ex, $"Error deleting metrics file '{file.FullName}'.");
                }
            }

            files = metricsDirectoryInfo.GetFiles().OrderBy(p => p.CreationTime).ToList();

            // return true if we have space for a new file
            return files.Count < _maxFileCount;
        }

        public async Task PublishMetricsAsync(object metrics)
        {
            string fileName = string.Empty;

            try
            {
                bool metricsPublishEnabled = !string.IsNullOrEmpty(MetricsFilePath);
                if (metricsPublishEnabled && !PrepareDirectoryForFile())
                {
                    return;
                }

                string metricsContent = JsonConvert.SerializeObject(metrics);
                _logger.PublishingMetrics(metricsContent);

                if (metricsPublishEnabled)
                {
                    fileName = $"{Guid.NewGuid().ToString().ToLower()}.json";
                    string filePath = Path.Combine(MetricsFilePath, fileName);

                    using (var streamWriter = _fileSystem.File.CreateText(filePath))
                    {
                        await streamWriter.WriteAsync(metricsContent);
                    }
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // TODO: consider using a retry strategy here
                _logger.LogError(ex, $"Error writing metrics file '{fileName}'.");
            }
        }
    }
}
