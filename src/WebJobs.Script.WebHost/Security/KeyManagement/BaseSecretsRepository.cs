// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public abstract class BaseSecretsRepository : ISecretsRepository, IDisposable
    {
        private readonly object _sentinelWatcherInitializationLock = new object();
        private readonly string _hostSecretsSentinelFilePath;
        private readonly string _secretsSentinelFilePath;

        private AutoRecoveringFileSystemWatcher _sentinelFileWatcher;
        private bool _disposing = false;
        private bool _disposed = false;

        public BaseSecretsRepository(string secretsSentinelFilePath, ILogger logger, IEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(secretsSentinelFilePath);
            ArgumentNullException.ThrowIfNull(logger);

            _secretsSentinelFilePath = secretsSentinelFilePath;
            Logger = logger;
            _hostSecretsSentinelFilePath = Path.Combine(_secretsSentinelFilePath, ScriptConstants.HostMetadataFileName);

            // Initialize sentinel watcher on a delay so we don't affect cold start.
            // this does open a small window for race conditions in multi-instance scenarios,
            // (e.g. if an instance has loaded/cached secrets but hasn't setup the watcher yet).
            // However, the "reload on failure" logic in SecretManager.GetAuthorizationLevelOrNullAsync
            // will handle those rare cases.
            Utility.ExecuteAfterColdStartDelay(environment, InitializeSentinelDirectoryAndWatcher);
        }

        public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        protected ILogger Logger { get; }

        public abstract bool IsEncryptionSupported { get; }

        public abstract string Name { get; }

        protected string GetSecretsSentinelFilePath(ScriptSecretsType secretsType, string functionName = null)
        {
            return secretsType == ScriptSecretsType.Host
                ? _hostSecretsSentinelFilePath
                : Path.Combine(_secretsSentinelFilePath, GetSecretFileName(functionName));
        }

        protected static string GetSecretFileName(string functionName)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.json", functionName.ToLowerInvariant());
        }

        protected void OnChanged(object sender, FileSystemEventArgs e)
        {
            var changeHandler = SecretsChanged;
            if (changeHandler != null)
            {
                var args = new SecretsChangedEventArgs { SecretsType = ScriptSecretsType.Host };

                if (!string.Equals(Path.GetFileName(e.FullPath), ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase))
                {
                    args.SecretsType = ScriptSecretsType.Function;
                    args.Name = Path.GetFileNameWithoutExtension(e.FullPath).ToLowerInvariant();
                }

                changeHandler(this, args);
            }
        }

        public abstract Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName);

        public abstract Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets);

        public abstract Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets);

        public abstract Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger);

        public abstract Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName);

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _disposing = true;
                    if (_sentinelFileWatcher != null)
                    {
                        lock (_sentinelWatcherInitializationLock)
                        {
                            _sentinelFileWatcher?.Dispose();
                            _sentinelFileWatcher = null;
                        }
                    }
                }

                _disposed = true;
            }
        }

        private void InitializeSentinelDirectoryAndWatcher()
        {
            if (!_disposing)
            {
                FileUtility.EnsureDirectoryExists(_secretsSentinelFilePath);

                lock (_sentinelWatcherInitializationLock)
                {
                    if (!_disposing)
                    {
                        _sentinelFileWatcher = new AutoRecoveringFileSystemWatcher(_secretsSentinelFilePath, "*.json");
                        _sentinelFileWatcher.Changed += OnChanged;
                    }
                }

                Logger.LogDebug($"Sentinel watcher initialized for path {_secretsSentinelFilePath}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}