// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class FileStorageSyncTriggerHashClient : ISyncTriggerHashClient
    {
        private readonly string _filePath;
        private readonly ILogger _logger;

        public FileStorageSyncTriggerHashClient(string filePath, ILogger logger)
        {
            _filePath = filePath;
            _logger = logger;
        }

        public async Task<string> CheckHashAsync(string content)
        {
            try
            {
                // compute the current hash value and compare it with
                // the last stored value
                string currentHash = null;
                using (var sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    currentHash = hash
                        .Aggregate(new StringBuilder(), (a, b) => a.Append(b.ToString("x2")))
                        .ToString();
                }

                // get the last hash value if present
                string lastHash = null;
                if (File.Exists(_filePath))
                {
                    lastHash = await File.ReadAllTextAsync(_filePath);
                    _logger.LogDebug($"SyncTriggers hash (Last='{lastHash}', Current='{currentHash}')");
                }

                if (string.Compare(currentHash, lastHash) != 0)
                {
                    // hash will need to be updated - return the
                    // new hash value
                    return currentHash;
                }
            }
            catch (Exception ex)
            {
                // best effort
                _logger.LogError(ex, "Error checking SyncTriggers hash");
            }

            // if the last and current hash values are the same,
            // or if any error occurs, return null
            return null;
        }

        public async Task UpdateHashAsync(string hash)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    string directoryPath = Path.GetDirectoryName(_filePath);
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                }

                await File.WriteAllTextAsync(_filePath, hash);
                _logger.LogDebug($"SyncTriggers hash updated to '{hash}'");
            }
            catch (Exception ex)
            {
                // best effort
                _logger.LogError(ex, "Error updating SyncTriggers hash");
            }
        }

        public async Task DeleteIfExistsAsync()
        {
            await Task.Run(() =>
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            });
        }

        public async Task<bool> ExistsAsync() => await Task.Run(() => File.Exists(_filePath));

        public async Task<string> GetHashAsync()
        {
            string result = string.Empty;
            if (File.Exists(_filePath))
            {
                result = await File.ReadAllTextAsync(_filePath);
            }

            return result;
        }
    }
}
