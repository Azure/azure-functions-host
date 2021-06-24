// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.StorageProvider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// This serves as a placeholder for a concrete implementation of a storage abstraction.
    /// This StorageProvider provides a wrapper to align all uses of storage by the Functions Host
    /// </summary>
    internal class AzureStorageProvider : IAzureStorageProvider
    {
        private readonly BlobServiceClientProvider _blobServiceClientProvider;
        private IOptionsMonitor<JobHostInternalStorageOptions> _storageOptions;
        private IConfiguration _configuration;
        private ILogger _logger;

        public AzureStorageProvider(IScriptHostManager scriptHostManager, IConfiguration configuration, BlobServiceClientProvider blobServiceClientProvider, IOptionsMonitor<JobHostInternalStorageOptions> options, ILogger<AzureStorageProvider> logger)
        {
            _blobServiceClientProvider = blobServiceClientProvider;
            _storageOptions = options;
            _logger = logger;

            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableMergedWebHostScriptHostConfiguration))
            {
                _configuration = configuration;
            }
            else
            {
                _ = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
                _configuration = new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .Add(new ActiveHostConfigurationSource(scriptHostManager))
                    .Build();
            }
        }

        /// <summary>
        /// Checks if a BlobServiceClient can be created (indicates the specified connection can be parsed)
        /// </summary>
        /// <param name="connection">connection to use for the BlobServiceClient</param>
        /// <returns>true if successful; false otherwise</returns>
        public bool ConnectionExists(string connection)
        {
            var section = _configuration.GetWebJobsConnectionStringSection(connection);
            return section != null && section.Exists();
        }

        /// <summary>
        /// Try create BlobServiceClient
        /// SDK parse connection string method can throw exceptions: https://github.com/Azure/azure-sdk-for-net/blob/master/sdk/storage/Azure.Storage.Common/src/Shared/StorageConnectionString.cs#L238
        /// </summary>
        /// <param name="client">client to instantiate</param>
        /// <param name="connectionString">connection string to use</param>
        /// <returns>successful client creation</returns>
        public virtual bool TryGetBlobServiceClientFromConnectionString(out BlobServiceClient client, string connectionString = null)
        {
            var connectionStringToUse = connectionString ?? _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
            return _blobServiceClientProvider.TryGetFromConnectionString(connectionStringToUse, out client);
        }

        /// <summary>
        /// Try create BlobServiceClient
        /// </summary>
        /// <param name="client">client to instantiate</param>
        /// <param name="connection">Name of the connection to use</param>
        /// <returns>successful client creation</returns>
        public virtual bool TryGetBlobServiceClientFromConnection(out BlobServiceClient client, string connection = null)
        {
            var connectionToUse = connection ?? ConnectionStringNames.Storage;
            return _blobServiceClientProvider.TryGet(connectionToUse, _configuration, out client);
        }

        public virtual BlobContainerClient GetBlobContainerClient()
        {
            if (_storageOptions?.CurrentValue.InternalSasBlobContainer != null)
            {
                return new BlobContainerClient(new Uri(_storageOptions.CurrentValue.InternalSasBlobContainer));
            }

            if (!TryGetBlobServiceClientFromConnection(out BlobServiceClient blobServiceClient, ConnectionStringNames.Storage))
            {
                throw new InvalidOperationException($"Could not create BlobServiceClient to obtain the BlobContainerClient using Connection: {ConnectionStringNames.Storage}");
            }

            return blobServiceClient.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName);
        }
    }
}
