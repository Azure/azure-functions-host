// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
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
    internal class HostAzureStorageProvider : AzureStorageProvider
    {
        private readonly BlobServiceClientProvider _blobServiceClientProvider;
        private IConfiguration _configuration;
        private ILogger _logger;

        public HostAzureStorageProvider(IScriptHostManager scriptHostManager, IConfiguration configuration, BlobServiceClientProvider blobServiceClientProvider, IOptionsMonitor<JobHostInternalStorageOptions> options, ILogger<AzureStorageProvider> logger) : base(configuration, blobServiceClientProvider, options)
        {
            _blobServiceClientProvider = blobServiceClientProvider;
            _logger = logger;

            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableMergedWebHostScriptHostConfiguration))
            {
                _configuration = configuration;
            }
            else
            {
                _ = scriptHostManager ?? throw new ArgumentNullException(nameof(scriptHostManager));
                _configuration = new ConfigurationBuilder()
                    .Add(new ActiveHostConfigurationSource(scriptHostManager))
                    .AddConfiguration(configuration)
                    .Build();
            }
        }

        /// <summary>
        /// Checks if a BlobServiceClient can be created (indicates the specified connection can be parsed)
        /// </summary>
        /// <param name="connection">connection to use for the BlobServiceClient</param>
        /// <returns>true if successful; false otherwise</returns>
        public override bool ConnectionExists(string connection)
        {
            var section = _configuration.GetWebJobsConnectionStringSection(connection);
            return section != null && section.Exists();
        }

        /// <summary>
        /// Try create BlobServiceClient
        /// </summary>
        /// <param name="client">client to instantiate</param>
        /// <param name="connection">Name of the connection to use</param>
        /// <returns>successful client creation</returns>
        public override bool TryGetBlobServiceClientFromConnection(out BlobServiceClient client, string connection = null)
        {
            var connectionToUse = connection ?? ConnectionStringNames.Storage;
            return _blobServiceClientProvider.TryGet(connectionToUse, _configuration, out client);
        }
    }
}
