// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

        public virtual BlobContainerClient GetWebJobsBlobContainerClient()
        {
            if (_storageOptions?.CurrentValue.InternalSasBlobContainer != null)
            {
                return new BlobContainerClient(new Uri(_storageOptions.CurrentValue.InternalSasBlobContainer));
            }

            if (!TryGetBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobServiceClient))
            {
                throw new InvalidOperationException($"Could not create BlobContainerClient in AzureStorageProvider using Connection: {ConnectionStringNames.Storage}");
            }

            return blobServiceClient.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName);
        }

        public virtual bool TryGetBlobServiceClientFromConnection(string connection, out BlobServiceClient client)
        {
            var connectionToUse = connection ?? ConnectionStringNames.Storage;

            try
            {
                client = _blobServiceClientProvider.Create(connectionToUse, Configuration);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogDebug($"Could not create BlobServiceClient in AzureStorageProvider. Exception: {e}");
                client = default;
                return false;
            }
        }
    }
}
