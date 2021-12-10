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
    /// <summary>
    /// An <see cref="ISecretsRepository"/> implementation that uses the file system as the backing store.
    /// </summary>
    public sealed class FileSystemSecretsRepository : BaseSecretsRepository
    {
        private readonly string _secretsPath;
        private readonly string _hostSecretsPath;
        private readonly int _retryCount = 5;
        private readonly int _retryDelay = 100;

        public FileSystemSecretsRepository(string secretsPath, ILogger logger, IEnvironment environment) : base(secretsPath, logger, environment)
        {
            ArgumentNullException.ThrowIfNull(secretsPath);

            _secretsPath = secretsPath;
            _hostSecretsPath = Path.Combine(_secretsPath, ScriptConstants.HostMetadataFileName);
        }

        public override bool IsEncryptionSupported
        {
            get
            {
                return false;
            }
        }

        public override string Name => nameof(FileSystemSecretsRepository);

        public override async Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName)
        {
            string filePath = GetSecretsFilePath(type, functionName);
            string secretsContent = null;
            if (File.Exists(filePath))
            {
                for (int currentRetry = 0; ; currentRetry++)
                {
                    try
                    {
                        // load the secrets file
                        secretsContent = await FileUtility.ReadAsync(filePath);
                        break;
                    }
                    catch (IOException)
                    {
                        if (currentRetry > _retryCount)
                        {
                            throw;
                        }
                    }
                    await Task.Delay(_retryDelay);
                }
            }
            return string.IsNullOrEmpty(secretsContent) ? null : ScriptSecretSerializer.DeserializeSecrets(type, secretsContent);
        }

        public override async Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            string filePath = GetSecretsFilePath(type, functionName);
            for (int currentRetry = 0; ; currentRetry++)
            {
                try
                {
                    await FileUtility.WriteAsync(filePath, ScriptSecretSerializer.SerializeSecrets(secrets));
                    break;
                }
                catch (IOException)
                {
                    if (currentRetry > _retryCount)
                    {
                        throw;
                    }
                }
                await Task.Delay(_retryDelay);
            }
        }

        public override async Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets)
        {
            string filePath = GetSecretsFilePath(type, functionName, true);
            await FileUtility.WriteAsync(filePath, ScriptSecretSerializer.SerializeSecrets(secrets));
        }

        public override async Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger)
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
                    if (string.Equals(secretFile.Name, ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase)
                        || secretFile.Name.Contains(ScriptConstants.Snapshot))
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
                            // destructive operation, thus log
                            string removeSecretsMessage = $"Deleting secret file {secretFile.FullName}";
                            logger.LogDebug(removeSecretsMessage);
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
                string message = "An error occurred while purging secret files";
                logger.LogError(0, ex, message);
            }
        }

        public override async Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName)
        {
            string prefix = Path.GetFileNameWithoutExtension(GetSecretsFilePath(type, functionName)) + $".{ScriptConstants.Snapshot}*";

            return await FileUtility.GetFilesAsync(Path.GetDirectoryName(_hostSecretsPath), prefix);
        }

        private string GetSecretsFilePath(ScriptSecretsType secretsType, string functionName = null, bool isSnapshot = false)
        {
            string result = secretsType == ScriptSecretsType.Host
                ? _hostSecretsPath
                : GetFunctionSecretsFilePath(functionName);

            if (isSnapshot)
            {
                result = SecretsUtility.GetNonDecryptableName(result);
            }

            return result;
        }

        private string GetFunctionSecretsFilePath(string functionName)
        {
            string secretFileName = string.Format(CultureInfo.InvariantCulture, "{0}.json", functionName);
            return Path.Combine(_secretsPath, secretFileName);
        }
    }
}