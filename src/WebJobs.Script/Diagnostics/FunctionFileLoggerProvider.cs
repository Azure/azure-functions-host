// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Provides a logger for writing Worker and Function logs to specific files.
    /// </summary>
    internal class FunctionFileLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, FileWriter> _fileWriterCache = new ConcurrentDictionary<string, FileWriter>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<bool> _isFileLoggingEnabled;
        private readonly Func<bool> _isPrimary;
        private readonly string _roogLogPath;
        private static readonly Regex _workerCategoryRegex = new Regex(@"^Worker\.[^\s]+\.[^\s]+");

        private bool _disposed = false;

        public FunctionFileLoggerProvider(string rootLogPath, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary)
        {
            _roogLogPath = rootLogPath;
            _isFileLoggingEnabled = isFileLoggingEnabled;
            _isPrimary = isPrimary;
        }

        // For testing
        internal IDictionary<string, FileWriter> FileWriterCache => _fileWriterCache;

        public ILogger CreateLogger(string categoryName)
        {
            string filePath = GetFilePath(categoryName);

            if (filePath != null)
            {
                // Make sure that we return the same fileWriter if multiple loggers write to the same path. This happens
                // with Function logs as Function.{FunctionName} and Function.{FunctionName}.User both go to the same file.
                FileWriter fileWriter = _fileWriterCache.GetOrAdd(filePath, (p) => new FileWriter(Path.Combine(_roogLogPath, filePath)));
                return new FileLogger(categoryName, fileWriter, _isFileLoggingEnabled, _isPrimary);
            }

            // If it's not a supported category, we won't log anything from this provider.
            return NullLogger.Instance;
        }

        internal static string GetFilePath(string categoryName)
        {
            // Supported category-to-path mappings:
            //   Worker.{Language}.{Id} -> Worker\{Language}
            //   Function.{FunctionName} -> Function\{FunctionName}
            //   Function.{FunctionName}.User -> Function\{FunctionName}
            string filePath = null;
            if (_workerCategoryRegex.IsMatch(categoryName) ||
                LogCategories.IsFunctionCategory(categoryName) ||
                LogCategories.IsFunctionUserCategory(categoryName))
            {
                string[] parts = categoryName.Split('.');
                filePath = Path.Combine(parts[0], parts[1]);
            }

            return filePath;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (string fileWriterKey in _fileWriterCache.Keys)
                {
                    if (_fileWriterCache.TryRemove(fileWriterKey, out FileWriter fileWriter))
                    {
                        fileWriter.Dispose();
                    }
                }

                _disposed = true;
            }
        }
    }
}
