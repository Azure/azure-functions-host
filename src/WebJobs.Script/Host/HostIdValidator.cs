// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Used to perform Host ID validation checks, ensuring that when hosts are sharing
    /// a storage account, their computed IDs don't collide.
    /// </summary>
    /// <remarks>
    /// <see cref="ScriptHostIdProvider"/> computes a Host ID and truncates it if needed to
    /// ensure it's under length limits. For two different Function Apps, this can result in
    /// both apps resolving to the same Host ID. This can cause problems if those apps share
    /// a storage account. This class helps detect/prevent such cases.
    /// </remarks>
    public class HostIdValidator
    {
        public const string BlobPathFormat = "ids/usage/{0}";
        private const LogLevel DefaultLevel = LogLevel.Error;

        private readonly IEnvironment _environment;
        private readonly IAzureBlobStorageProvider _azureBlobStorageProvider;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly HostNameProvider _hostNameProvider;
        private readonly ILogger _logger;

        private readonly object _syncLock = new object();
        private bool _validationScheduled;

        public HostIdValidator(IEnvironment environment, IAzureBlobStorageProvider azureBlobStorageProvider, IApplicationLifetime applicationLifetime,
            HostNameProvider hostNameProvider, ILogger<HostIdValidator> logger)
        {
            _environment = environment;
            _azureBlobStorageProvider = azureBlobStorageProvider;
            _applicationLifetime = applicationLifetime;
            _hostNameProvider = hostNameProvider;
            _logger = logger;
        }

        internal bool ValidationScheduled => _validationScheduled;

        public virtual void ScheduleValidation(string hostId)
        {
            lock (_syncLock)
            {
                if (!_validationScheduled)
                {
                    // Schedule the validation to run asynchronously after a delay. This delay ensures
                    // we're not impacting coldstart.
                    Utility.ExecuteAfterColdStartDelay(_environment, () => Task.Run(() => ValidateHostIdUsageAsync(hostId)));
                    _validationScheduled = true;
                }
            }
        }

        internal async Task ValidateHostIdUsageAsync(string hostId)
        {
            try
            {
                if (!_azureBlobStorageProvider.TryCreateHostingBlobContainerClient(out var blobContainerClient))
                {
                    return;
                }

                HostIdInfo hostIdInfo = await ReadHostIdInfoAsync(hostId, blobContainerClient);

                if (hostIdInfo != null)
                {
                    // an existing record exists for this host ID
                    CheckForCollision(hostId, hostIdInfo);
                }
                else
                {
                    // no existing record, so write one, claiming this host ID for this host name
                    // in this storage account
                    hostIdInfo = new HostIdInfo
                    {
                        Hostname = _hostNameProvider.Value
                    };
                    await WriteHostIdAsync(hostId, hostIdInfo, blobContainerClient);
                }
            }
            catch (Exception ex)
            {
                // best effort - log error and continue
                _logger.LogError(ex, "Error validating host ID usage.");
            }
        }

        private void CheckForCollision(string hostId, HostIdInfo hostIdInfo)
        {
            // verify the host name is the same as our host name
            if (!string.Equals(_hostNameProvider.Value, hostIdInfo.Hostname, StringComparison.OrdinalIgnoreCase))
            {
                HandleCollision(hostId);
            }
        }

        private void HandleCollision(string hostId)
        {
            // see if the user has specified a level, otherwise default
            string value = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsHostIdCheckLevel);
            if (!Enum.TryParse<LogLevel>(value, out LogLevel level))
            {
                level = DefaultLevel;
            }

            string message = string.Format(Resources.HostIdCollisionFormat, hostId);

            if (level == LogLevel.Warning)
            {
                _logger.LogWarning(message);

                DiagnosticEventLoggerExtensions.LogDiagnosticEventInformation(_logger, DiagnosticEventConstants.HostIdCollisionErrorCode, message, DiagnosticEventConstants.HostIdCollisionHelpLink);
            }
            else
            {
                // We only allow Warning/Error levels to be specified,
                // so anything other than Warning is treated as Error.
                _logger.LogError(message);

                DiagnosticEventLoggerExtensions.LogDiagnosticEventError(_logger, DiagnosticEventConstants.HostIdCollisionErrorCode, message, DiagnosticEventConstants.HostIdCollisionHelpLink, new InvalidOperationException(message));

                _applicationLifetime.StopApplication();
            }
        }

        internal async Task WriteHostIdAsync(string hostId, HostIdInfo hostIdInfo, BlobContainerClient blobContainerClient)
        {
            try
            {
                string blobPath = string.Format(BlobPathFormat, hostId);
                BlobClient blobClient = blobContainerClient.GetBlobClient(blobPath);
                BinaryData data = BinaryData.FromObjectAsJson(hostIdInfo);
                await blobClient.UploadAsync(data);

                _logger.LogDebug($"Host ID record written (ID:{hostId}, HostName:{hostIdInfo.Hostname})");
            }
            catch (RequestFailedException rfex) when (rfex.Status == 409)
            {
                // Another instance wrote the blob between the time when we initially
                // checked and when we attempted to write. Read the blob and validate it.
                hostIdInfo = await ReadHostIdInfoAsync(hostId, blobContainerClient);
                if (hostIdInfo != null)
                {
                    CheckForCollision(hostId, hostIdInfo);
                }
            }
            catch (Exception ex)
            {
                // best effort
                _logger.LogError(ex, "Error writing host ID info");
            }
        }

        internal async Task<HostIdInfo> ReadHostIdInfoAsync(string hostId, BlobContainerClient blobContainerClient)
        {
            HostIdInfo hostIdInfo = null;

            try
            {
                // check storage to see if a record already exists for this host ID
                string blobPath = string.Format(BlobPathFormat, hostId);
                BlobClient blobClient = blobContainerClient.GetBlobClient(blobPath);
                var downloadResponse = await blobClient.DownloadAsync();
                string content;
                using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
                {
                    content = reader.ReadToEnd();
                }

                if (!string.IsNullOrEmpty(content))
                {
                    hostIdInfo = JsonConvert.DeserializeObject<HostIdInfo>(content);
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                // no record stored for this host ID
            }
            catch (Exception ex)
            {
                // best effort
                _logger.LogError(ex, "Error reading host ID info");
            }

            return hostIdInfo;
        }

        internal class HostIdInfo
        {
            public string Hostname { get; set; }
        }
    }
}
