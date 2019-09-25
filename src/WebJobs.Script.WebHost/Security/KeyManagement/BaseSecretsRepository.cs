// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public abstract class BaseSecretsRepository : ISecretsRepository, IDisposable
    {
        private readonly AutoRecoveringFileSystemWatcher _sentinelFileWatcher;
        private readonly string _hostSecretsSentinelFilePath;
        private readonly string _secretsSentinelFilePath;

        private bool _disposed = false;

        public BaseSecretsRepository(string secretsSentinelFilePath)
        {
            if (secretsSentinelFilePath == null)
            {
                throw new ArgumentNullException(nameof(secretsSentinelFilePath));
            }
            _secretsSentinelFilePath = secretsSentinelFilePath;
            _hostSecretsSentinelFilePath = Path.Combine(_secretsSentinelFilePath, ScriptConstants.HostMetadataFileName);

            Directory.CreateDirectory(_secretsSentinelFilePath);

            _sentinelFileWatcher = new AutoRecoveringFileSystemWatcher(_secretsSentinelFilePath, "*.json");
            _sentinelFileWatcher.Changed += OnChanged;
        }

        public BaseSecretsRepository(string secretsSentinelFilePath, ILogger logger) : this(secretsSentinelFilePath)
        {
            Logger = logger;
        }

        public event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        protected ILogger Logger { get; }

        public abstract bool IsEncryptionSupported { get; }

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

                if (string.Compare(Path.GetFileName(e.FullPath), ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) != 0)
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
                    _sentinelFileWatcher.Dispose();
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