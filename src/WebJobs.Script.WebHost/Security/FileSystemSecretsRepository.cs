﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.IO;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses the file system as the backing store.
    /// </summary>
    public sealed class FileSystemSecretsRepository : ISecretsRepository, IDisposable
    {
        private readonly string _secretsPath;
        private readonly string _hostSecretsPath;
        private readonly AutoRecoveringFileSystemWatcher _fileWatcher;
        private bool _disposed = false;

        public FileSystemSecretsRepository(string secretsPath)
        {
            if (secretsPath == null)
            {
                throw new ArgumentNullException(nameof(secretsPath));
            }

            _secretsPath = secretsPath;
            _hostSecretsPath = Path.Combine(_secretsPath, ScriptConstants.HostMetadataFileName);

            Directory.CreateDirectory(_secretsPath);

            _fileWatcher = new AutoRecoveringFileSystemWatcher(_secretsPath, "*.json");
            _fileWatcher.Changed += OnChanged;
        }

        public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        private string GetSecretsFilePath(ScriptSecretsType secretsType, string functionName = null)
        {
            return secretsType == ScriptSecretsType.Host
                ? _hostSecretsPath
                : GetFunctionSecretsFilePath(functionName);
        }

        private string GetFunctionSecretsFilePath(string functionName)
        {
            string secretFileName = string.Format(CultureInfo.InvariantCulture, "{0}.json", functionName);
            return Path.Combine(_secretsPath, secretFileName);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            var changeHandler = SecretsChanged;
            if (changeHandler != null)
            {
                var args = new SecretsChangedEventArgs { SecretsType = ScriptSecretsType.Host };

                if (string.Compare(Path.GetFileName(e.FullPath), ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    args.SecretsType = ScriptSecretsType.Function;
                    args.Name = Path.GetFileNameWithoutExtension(e.FullPath).ToLowerInvariant();
                }

                changeHandler(this, args);
            }
        }

        public async Task<string> ReadAsync(ScriptSecretsType type, string functionName)
        {
            string filePath = GetSecretsFilePath(type, functionName);
            string secretsContent = null;
            if (File.Exists(filePath))
            {
                // load the secrets file
                secretsContent = await FileUtility.ReadAsync(filePath);
            }
            return secretsContent;
        }

        public async Task WriteAsync(ScriptSecretsType type, string functionName, string secretsContent)
        {
            string filePath = GetSecretsFilePath(type, functionName);
            await FileUtility.WriteAsync(filePath, secretsContent);
        }

        public async Task PurgeOldSecretsAsync(IList<string> currentFunctions, TraceWriter traceWriter)
        {
            try
            {
                var secretsDirectory = new DirectoryInfo(_secretsPath);
                if (!Directory.Exists(_secretsPath))
                {
                    return;
                }

                foreach (var secretFile in secretsDirectory.GetFiles("*.json"))
                {
                    if (string.Compare(secretFile.Name, ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        // the secrets directory contains the host secrets file in addition
                        // to function secret files
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(secretFile.Name);
                    if (!currentFunctions.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await FileUtility.DeleteIfExistsAsync(secretFile.FullName);
                        }
                        catch
                        {
                            // Purge is best effort
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Purge is best effort
                traceWriter.Error("An error occurred while purging secret files", ex);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileWatcher.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}