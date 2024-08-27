// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Pass-through class to create a <see cref="BlobServiceClient"/> using <see cref="AzureComponentFactory"/>.
    /// This class uses additional configuration settings for a specified connection in addition to those in <see cref="AzureComponentFactory"/>.
    /// To support scenarios where a storage connection (i.e. AzureWebJobsStorage) needs to reference multiple serviceUris (blob and queue),
    /// properties in <see cref="StorageServiceUriOptions"/> are supported to create the Azure blob service URI.
    /// </summary>
    internal class BlobServiceClientProvider : StorageClientProvider<BlobServiceClient, BlobClientOptions>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobServiceClientProvider"/> class that uses the registered Azure services to create a BlobServiceClient.
        /// </summary>
        /// <param name="componentFactory">The Azure factory responsible for creating clients. <see cref="AzureComponentFactory"/></param>
        /// <param name="logForwarder">Log forwarder that forwards events to ILogger. <see cref="AzureEventSourceLogForwarder"/></param>
        public BlobServiceClientProvider(AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder)
            : base(componentFactory, logForwarder) { }

        /// <summary>
        /// Provides logic to create a <see cref="BlobServiceClient"/> from an <see cref="IConfiguration"/> source. This class
        /// supports using configuration settings (AccountName and BlobServiceUri) in <see cref="StorageServiceUriOptions"/> in
        /// addition to those in <see cref="AzureComponentFactory"/>.
        /// If no serviceUri can be constructed using the additional settings, this method falls back to the logic in
        /// <see cref="AzureComponentFactory"/>.
        /// </summary>
        /// <param name="configuration">Configuration to retrieve settings from.</param>
        /// <param name="tokenCredential">Credential for the client.</param>
        /// <param name="options">Options to configure the client.</param>
        /// <returns>An instance of <see cref="BlobServiceClient"/></returns>
        protected override BlobServiceClient CreateClient(IConfiguration configuration, TokenCredential tokenCredential, BlobClientOptions options)
        {
            // If connection string is present, it will be honored first; bypass creating a serviceUri
            if (!IsConnectionStringPresent(configuration))
            {
                var serviceUri = configuration.Get<StorageServiceUriOptions>().GetBlobServiceUri();
                if (serviceUri != null)
                {
                    return new BlobServiceClient(serviceUri, tokenCredential, options);
                }
            }

            return base.CreateClient(configuration, tokenCredential, options);
        }
    }
}
