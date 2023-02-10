// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxAppServiceFileLogger : ILinuxAppServiceFileLogger
    {
        private readonly string _logFileName;
        private readonly string _logFileDirectory;
        private readonly string _logFilePath;
        private readonly BlockingCollection<string> _buffer;
        private readonly List<string> _currentBatch;
        private readonly IFileSystem _fileSystem;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private Task _outputTask;
        private bool _logBackOffEnabled;

        public LinuxAppServiceFileLogger(string logFileName, string logFileDirectory, IFileSystem fileSystem, bool logBackoffEnabled = false, bool startOnCreate = true)
        {
            _logFileName = logFileName;
            _logFileDirectory = logFileDirectory;
            _logFilePath = Path.Combine(_logFileDirectory, _logFileName + ".log");
            _buffer = new BlockingCollection<string>(new ConcurrentQueue<string>());
            _logBackOffEnabled = logBackoffEnabled;
            _currentBatch = new List<string>();
            _fileSystem = fileSystem;
            _cancellationTokenSource = new CancellationTokenSource();

            if (startOnCreate)
            {
                Start();
            }
        }

        // Maximum number of files
        public int MaxFileCount { get; set; } = 3;

        // Maximum size of individual log file in MB
        public int MaxFileSizeMb { get; set; } = 10;

        public virtual void Log(string message)
        {
            try
            {
                _buffer.Add(message);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public void Start()
        {
            if (_outputTask == null)
            {
                _outputTask = Task.Factory.StartNew(ProcessLogQueue, null, TaskCreationOptions.LongRunning);
            }
        }

        public void Stop(TimeSpan timeSpan)
        {
            _cancellationTokenSource.Cancel();

            try
            {
                _outputTask?.Wait(timeSpan);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public virtual async Task ProcessLogQueue(object state)
        {
            int maxFlushFrequencySeconds = 30;
            int currentFlushFrequencySeconds = _logBackOffEnabled ? 1 : maxFlushFrequencySeconds;

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                await InternalProcessLogQueue();
                await Task.Delay(TimeSpan.FromSeconds(currentFlushFrequencySeconds), _cancellationTokenSource.Token).ContinueWith(task => { });
                if (currentFlushFrequencySeconds < maxFlushFrequencySeconds)
                {
                    currentFlushFrequencySeconds = Math.Min(maxFlushFrequencySeconds, currentFlushFrequencySeconds * 2);
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }

        // internal for unittests
        internal async Task InternalProcessLogQueue()
        {
            string currentMessage;
            while (_buffer.TryTake(out currentMessage))
            {
                _currentBatch.Add(currentMessage);
            }

            if (_currentBatch.Any())
            {
                try
                {
                    await WriteLogs(_currentBatch);
                }
                catch (Exception)
                {
                    // Ignored
                }

                _currentBatch.Clear();
            }
        }

        private async Task WriteLogs(IEnumerable<string> currentBatch)
        {
            _fileSystem.Directory.CreateDirectory(_logFileDirectory);

            var fileInfo = _fileSystem.FileInfo.FromFileName(_logFilePath);
            if (fileInfo.Exists)
            {
                if (fileInfo.Length / (1024 * 1024) >= MaxFileSizeMb)
                {
                    RollFiles();
                }
            }

            await AppendLogs(_logFilePath, currentBatch);
        }

        private async Task AppendLogs(string filePath, IEnumerable<string> logs)
        {
            using (var streamWriter = _fileSystem.File.AppendText(filePath))
            {
                foreach (var log in logs)
                {
                    await streamWriter.WriteLineAsync(log);
                }
            }
        }

        private void RollFiles()
        {
            // Rename current file to older file.
            // Empty current file.
            // Delete oldest file if exceeded configured max no. of files.

            _fileSystem.File.Move(_logFilePath, GetCurrentFileName(DateTime.UtcNow));

            var fileInfoBases = ListFiles(_logFileDirectory, _logFileName + "*", SearchOption.TopDirectoryOnly);

            if (fileInfoBases.Length >= MaxFileCount)
            {
                var oldestFile = fileInfoBases.OrderByDescending(f => f.Name).Last();
                oldestFile.Delete();
            }
        }

        private FileInfoBase[] ListFiles(string directoryPath, string pattern, SearchOption searchOption)
        {
            return _fileSystem.DirectoryInfo.FromDirectoryName(directoryPath).GetFiles(pattern, searchOption);
        }

        public string GetCurrentFileName(DateTime dateTime)
        {
            return Path.Combine(_logFileDirectory, $"{_logFileName}{dateTime:yyyyMMddHHmmss}.log");
        }
    }
}
