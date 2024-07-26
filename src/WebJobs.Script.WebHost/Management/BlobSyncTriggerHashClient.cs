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
    public class BlobSyncTriggerHashClient : ISyncTriggerHashClient
    {
        private readonly BlobClient _blobClient;
        private readonly ILogger _logger;

        public BlobSyncTriggerHashClient(BlobClient blobClient, ILogger logger)
        {
            _blobClient = blobClient;
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
                if (await _blobClient.ExistsAsync())
                {
                    var downloadResponse = await _blobClient.DownloadAsync();
                    using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
                    {
                        lastHash = reader.ReadToEnd();
                    }
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
                // hash value has changed or was not yet stored
                // update the last hash value in storage
                using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(hash)))
                {
                    await _blobClient.UploadAsync(stream, overwrite: true);
                }
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
            await _blobClient.DeleteIfExistsAsync();
        }

        public async Task<bool> ExistsAsync() => await _blobClient.ExistsAsync();

        public async Task<string> GetHashAsync()
        {
            string result = string.Empty;
            var downloadResponse = await _blobClient.DownloadAsync();
            using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
            {
                result = reader.ReadToEnd();
            }

            return result;
        }
    }
}
