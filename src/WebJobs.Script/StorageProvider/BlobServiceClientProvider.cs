// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.StorageProvider
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// Provider to create BlobServiceClient objects
    /// </summary>
    internal class BlobServiceClientProvider : StorageClientProvider<BlobServiceClient, BlobClientOptions>
    {
        public BlobServiceClientProvider(AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder, ILogger<BlobServiceClientProvider> logger)
            : base(componentFactory, logForwarder, logger) { }

        /// <inheritdoc/>
        protected override string ServiceUriSubDomain
        {
            get
            {
                return "blob";
            }
        }

        /// <inheritdoc/>
        protected override BlobServiceClient CreateClient(IConfiguration configuration, TokenCredential tokenCredential, BlobClientOptions options)
        {
            // If connection string is present, it will be honored first
            if (!IsConnectionStringPresent(configuration) && TryGetServiceUri(configuration, out Uri serviceUri))
            {
                return new BlobServiceClient(serviceUri, tokenCredential, options);
            }

            return base.CreateClient(configuration, tokenCredential, options);
        }

        /// <inheritdoc/>
        protected override BlobServiceClient CreateClient(string connectionString)
        {
            return new BlobServiceClient(connectionString);
        }
    }
}
