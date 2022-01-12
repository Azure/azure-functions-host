// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Provides a logger for writing Worker and Function logs to specific files.
    /// </summary>
    internal class FunctionFileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentDictionary<string, IFileWriter> _fileWriterCache = new ConcurrentDictionary<string, IFileWriter>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<bool> _isFileLoggingEnabled;
        private readonly Func<bool> _isPrimary;
        private readonly string _rootLogPath;
        private readonly string _hostInstanceId;
        private static readonly Regex _workerCategoryRegex = new Regex(@"^Worker\.[^\s]+\.[^\s]+");
        private readonly IFileWriterFactory _fileWriterFactory;
        private IExternalScopeProvider _scopeProvider;
        private bool _disposed = false;

        public FunctionFileLoggerProvider(IOptions<ScriptJobHostOptions> scriptOptions, IFileLoggingStatusManager fileLoggingStatusManager,
            IPrimaryHostStateProvider primaryHostStateProvider, IFileWriterFactory fileWriterFactory)
        {
            _rootLogPath = scriptOptions.Value.RootLogPath;
            _isFileLoggingEnabled = () => fileLoggingStatusManager.IsFileLoggingEnabled;
            _isPrimary = () => primaryHostStateProvider.IsPrimary;
            _hostInstanceId = scriptOptions.Value.InstanceId;
            _fileWriterFactory = fileWriterFactory ?? throw new ArgumentNullException(nameof(fileWriterFactory));
        }

        // For testing
        internal IDictionary<string, IFileWriter> FileWriterCache => _fileWriterCache;

        public ILogger CreateLogger(string categoryName)
        {
            string filePath = GetFilePath(categoryName);

            if (filePath != null)
            {
                // Make sure that we return the same fileWriter if multiple loggers write to the same path. This happens
                // with Function logs as Function.{FunctionName} and Function.{FunctionName}.User both go to the same file.
                IFileWriter fileWriter = _fileWriterCache.GetOrAdd(filePath, (path) => _fileWriterFactory.Create(Path.Combine(_rootLogPath, path)));
                return new FileLogger(categoryName, fileWriter, _isFileLoggingEnabled, _isPrimary, LogType.Function, _scopeProvider);
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

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (string fileWriterKey in _fileWriterCache.Keys)
                {
                    if (_fileWriterCache.TryRemove(fileWriterKey, out IFileWriter fileWriter))
                    {
                        (fileWriter as IDisposable)?.Dispose();
                    }
                }

                _disposed = true;
            }
        }
    }
}
