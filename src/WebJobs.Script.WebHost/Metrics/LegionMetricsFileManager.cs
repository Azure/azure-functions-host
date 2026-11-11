// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    internal sealed class LegionMetricsFileManager
    {
        internal const string StagingDirectoryName = ".staging";
        private static readonly TimeSpan StagedFileRetentionPeriod = TimeSpan.FromMinutes(1);

        private readonly IFileSystem _fileSystem;
        private readonly int _maxFileCount;
        private readonly ILogger _logger;
        private readonly JsonSerializerSettings _serializerSettings;
        private readonly SemaphoreSlim _workerPublishLock = new(1, 1);
        private DateTime _lastWorkerFileCreationTimeUtc = DateTime.MinValue;
        private bool _workerFilePublicationBlocked;

        public LegionMetricsFileManager(string metricsFilePath, IFileSystem fileSystem, ILogger logger, int maxFileCount)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            MetricsFilePath = metricsFilePath;
            _maxFileCount = maxFileCount;
            GetCreationTimeUtc = path => _fileSystem.File.GetCreationTimeUtc(path);

            _serializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        // Internal access for testing only
        internal string MetricsFilePath { get; set;  }

        // Test seams for the Worker metrics creation-time ordering.
        internal Func<string, DateTime> GetCreationTimeUtc { get; set; }

        internal Func<TimeSpan, Task> DelayAsync { get; set; } = Task.Delay;

        internal int MaxWorkerFileAttempts { get; set; } = 25;

        internal TimeSpan WorkerFileRetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

        internal void DeleteExpiredWorkerMetrics(DateTime utcNow, TimeSpan retentionPeriod)
        {
            if (string.IsNullOrEmpty(MetricsFilePath))
            {
                return;
            }

            try
            {
                if (!_fileSystem.Directory.Exists(MetricsFilePath))
                {
                    return;
                }

                int deletedCount = 0;
                foreach (var file in _fileSystem.DirectoryInfo.FromDirectoryName(MetricsFilePath).GetFiles("*.json"))
                {
                    if (!IsWorkerMetricsFile(file.Name))
                    {
                        continue;
                    }

                    try
                    {
                        if (utcNow - file.LastWriteTimeUtc > retentionPeriod)
                        {
                            file.Delete();
                            deletedCount++;
                        }
                    }
                    catch (Exception ex) when (!ex.IsFatal())
                    {
                        _logger.LogError(ex, "Error deleting expired Worker metrics file '{FilePath}'.", file.FullName);
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.LogDebug("Deleted {FileCount} expired Worker metrics file(s). Retention period: {RetentionPeriod}.",
                        deletedCount, retentionPeriod);
                }

                if (_workerPublishLock.Wait(0))
                {
                    try
                    {
                        DeleteStaleStagedFiles(utcNow);
                    }
                    finally
                    {
                        _workerPublishLock.Release();
                    }
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                _logger.LogError(ex, "Error cleaning expired Worker metrics files in '{MetricsFilePath}'.", MetricsFilePath);
            }
        }

        // Staged files are normally moved or deleted immediately; this removes leftovers from an interrupted publish.
        private void DeleteStaleStagedFiles(DateTime utcNow)
        {
            string stagingPath = Path.Combine(MetricsFilePath, StagingDirectoryName);
            if (!_fileSystem.Directory.Exists(stagingPath))
            {
                return;
            }

            foreach (var file in _fileSystem.DirectoryInfo.FromDirectoryName(stagingPath).GetFiles("*.tmp"))
            {
                try
                {
                    if (utcNow - file.LastWriteTimeUtc > StagedFileRetentionPeriod)
                    {
                        file.Delete();
                    }
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    _logger.LogError(ex, "Error deleting stale staged Worker metrics file '{FilePath}'.", file.FullName);
                }
            }
        }

        private static bool IsWorkerMetricsFile(string name)
        {
            const int hashLength = 64;
            const int guidLength = 36;
            if (name.Length != hashLength + 1 + guidLength + ".json".Length ||
                name[hashLength] != '-' || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(name.AsSpan(hashLength + 1, guidLength), "D", out _))
            {
                return false;
            }

            foreach (char character in name.AsSpan(0, hashLength))
            {
                if (!char.IsAsciiHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private bool PrepareDirectoryForFile(string filePrefix)
        {
            if (string.IsNullOrEmpty(MetricsFilePath))
            {
                return false;
            }

            // ensure the directory exists
            var metricsDirectoryInfo = _fileSystem.Directory.CreateDirectory(MetricsFilePath);

            // ensure we're under the max file count
            string searchPattern = string.IsNullOrEmpty(filePrefix) ? "*" : $"{filePrefix}*.json";
            var files = metricsDirectoryInfo.GetFiles(searchPattern).OrderBy(p => p.CreationTime).ToList();
            if (files.Count < _maxFileCount)
            {
                return true;
            }

            // we're at or over limit
            // delete enough files that we have space to write a new one
            int numToDelete = files.Count - _maxFileCount + 1;
            var filesToDelete = files.Take(numToDelete).ToArray();

            _logger.LogDebug($"Deleting {filesToDelete.Length} metrics file(s).");

            Parallel.ForEach(filesToDelete, file =>
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
            });

            files = metricsDirectoryInfo.GetFiles(searchPattern).OrderBy(p => p.CreationTime).ToList();

            // return true if we have space for a new file
            return files.Count < _maxFileCount;
        }

        public async Task PublishMetricsAsync(object metrics, string instanceId = null)
        {
            string fileName = string.Empty;

            try
            {
                bool metricsPublishEnabled = !string.IsNullOrEmpty(MetricsFilePath);
                string filePrefix = instanceId is null ? string.Empty : $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId)))}-";
                if (metricsPublishEnabled && !PrepareDirectoryForFile(filePrefix))
                {
                    return;
                }

                string metricsContent = JsonConvert.SerializeObject(metrics, _serializerSettings);
                _logger.PublishingMetrics(metricsContent);

                if (metricsPublishEnabled)
                {
                    fileName = $"{filePrefix}{Guid.NewGuid().ToString().ToLowerInvariant()}.json";
                    string filePath = Path.Combine(MetricsFilePath, fileName);

                    using var streamWriter = _fileSystem.File.CreateText(filePath);
                    await streamWriter.WriteAsync(metricsContent);
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // TODO: consider using a retry strategy here
                _logger.LogError(ex, $"Error writing metrics file '{fileName}'.");
            }
        }

        /// <summary>
        /// Stages a per-Worker metrics file and publishes it only when its observed creation time is later than the
        /// staged creation time of the last Worker file successfully moved by this manager. Publication is deferred
        /// if the timestamp does not advance within the bounded retries. This does not guarantee the timestamp seen
        /// by Legion after the move or across Runtime processes.
        /// </summary>
        /// <returns>True when the file is published or file publication is disabled; otherwise, false.</returns>
        internal async Task<bool> PublishWorkerMetricsAsync(object metrics, string instanceId)
        {
            ArgumentNullException.ThrowIfNull(instanceId);

            string fileName = string.Empty;
            string stagedFilePath = null;
            bool lockTaken = false;
            bool moveStarted = false;

            try
            {
                await _workerPublishLock.WaitAsync();
                lockTaken = true;

                if (_workerFilePublicationBlocked)
                {
                    return false;
                }

                string metricsContent = JsonConvert.SerializeObject(metrics, _serializerSettings);
                if (string.IsNullOrEmpty(MetricsFilePath))
                {
                    _logger.PublishingMetrics(metricsContent);
                    return true;
                }

                string filePrefix = $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId)))}-";
                string stagingPath = Path.Combine(MetricsFilePath, StagingDirectoryName);
                _fileSystem.Directory.CreateDirectory(stagingPath);

                DateTime creationTimeUtc = DateTime.MinValue;
                int maxAttempts = Math.Max(1, MaxWorkerFileAttempts);
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    stagedFilePath = Path.Combine(stagingPath, $"{Guid.NewGuid().ToString().ToLowerInvariant()}.tmp");
                    using (var streamWriter = _fileSystem.File.CreateText(stagedFilePath))
                    {
                        await streamWriter.WriteAsync(metricsContent);
                    }

                    creationTimeUtc = GetCreationTimeUtc(stagedFilePath);
                    if (creationTimeUtc > _lastWorkerFileCreationTimeUtc)
                    {
                        if (!PrepareDirectoryForFile(filePrefix))
                        {
                            _logger.LogWarning("Deferring Worker metrics publication because the file limit could not be enforced for '{FilePrefix}'.", filePrefix);
                            return false;
                        }

                        _logger.PublishingMetrics(metricsContent);
                        fileName = $"{filePrefix}{Guid.NewGuid().ToString().ToLowerInvariant()}.json";
                        moveStarted = true;
                        _fileSystem.File.Move(stagedFilePath, Path.Combine(MetricsFilePath, fileName));
                        stagedFilePath = null;
                        _lastWorkerFileCreationTimeUtc = creationTimeUtc;
                        return true;
                    }

                    if (attempt < maxAttempts)
                    {
                        _fileSystem.File.Delete(stagedFilePath);
                        stagedFilePath = null;
                        await DelayAsync(WorkerFileRetryDelay);
                    }
                }

                _logger.LogWarning("Deferring Worker metrics publication because creation time {CreationTimeUtc:O} is not later than the previous Worker metrics file ({PreviousCreationTimeUtc:O}) after {AttemptCount} attempt(s).",
                    creationTimeUtc, _lastWorkerFileCreationTimeUtc, maxAttempts);
                return false;
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                if (moveStarted)
                {
                    _workerFilePublicationBlocked = true;
                    _logger.LogError(ex, "Unable to determine whether Worker metrics file '{FileName}' was published. Pausing Worker metrics publication to avoid a duplicate; pending metrics are retained only in memory and will be lost on process exit. Investigate before restarting.", fileName);
                }
                else
                {
                    _logger.LogError(ex, "Error writing Worker metrics file '{FileName}'; publication will be retried.", fileName);
                }

                return false;
            }
            finally
            {
                TryDeleteStagedFile(stagedFilePath);
                if (lockTaken)
                {
                    _workerPublishLock.Release();
                }
            }
        }

        private void TryDeleteStagedFile(string stagedFilePath)
        {
            if (stagedFilePath is null)
            {
                return;
            }

            try
            {
                _fileSystem.File.Delete(stagedFilePath);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                _logger.LogError(ex, "Error deleting staged Worker metrics file '{FilePath}'.", stagedFilePath);
            }
        }
    }
}