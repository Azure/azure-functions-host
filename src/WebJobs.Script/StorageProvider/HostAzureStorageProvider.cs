// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// This StorageProvider provides a wrapper to align all uses of storage blobs by the Functions Host.
    /// The Host overrides the implementation in Microsoft.Azure.WebJobs.Host.Storage to provide the IConfiguration that
    /// unifies the WebHost and inner ScriptHost configurations. This unification is done through ActiveHostConfigurationSource.
    /// </summary>
    internal class HostAzureStorageProvider : IAzureBlobStorageProvider
    {
        private readonly BlobServiceClientProvider _blobServiceClientProvider;
        private readonly ILogger<HostAzureStorageProvider> _logger;
        private readonly IOptionsMonitor<JobHostInternalStorageOptions> _storageOptions;

        public HostAzureStorageProvider(IScriptHostManager scriptHostManager, IConfiguration configuration, IOptionsMonitor<JobHostInternalStorageOptions> options, ILogger<HostAzureStorageProvider> logger, AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder)
        {
            _storageOptions = options;
            _logger = logger;

            _blobServiceClientProvider = new BlobServiceClientProvider(componentFactory, logForwarder);

            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableMergedWebHostScriptHostConfiguration))
            {
                Configuration = configuration;
            }
            else
            {
                _ = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
                Configuration = new ConfigurationBuilder()
                    .Add(new ActiveHostConfigurationSource(scriptHostManager))
                    .AddConfiguration(configuration)
                    .Build();
            }
        }

        public IConfiguration Configuration { get; private set; }

        public virtual bool TryCreateHostingBlobContainerClient(out BlobContainerClient blobContainerClient)
        {
            if (_storageOptions?.CurrentValue.InternalSasBlobContainer != null)
            {
                blobContainerClient = new BlobContainerClient(new Uri(_storageOptions.CurrentValue.InternalSasBlobContainer));
                _logger.LogDebug($"Using storage account {blobContainerClient.AccountName} and container {blobContainerClient.Name} for hosting BlobContainerClient.");
                return true;
            }

            if (!TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient))
            {
                _logger.LogDebug($"Could not create BlobContainerClient using Connection: {ConnectionStringNames.Storage}");
                blobContainerClient = default;
                return false;
            }

            blobContainerClient = blobServiceClient.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName);
            return true;
        }

        public virtual bool TryCreateBlobServiceClientFromConnection(string connection, out BlobServiceClient client)
        {
            var connectionToUse = connection ?? ConnectionStringNames.Storage;

            try
            {
                client = _blobServiceClientProvider.Create(connectionToUse, Configuration);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogDebug($"Could not create BlobServiceClient. Exception: {e}");
                client = default;
                return false;
            }
        }
    }
}
