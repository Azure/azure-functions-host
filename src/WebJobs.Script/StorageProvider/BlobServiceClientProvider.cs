// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
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

        /// <summary>
        /// An options class that constructs a storage service URI from a different properties.
        /// These properties are specific to WebJobs, as there may be other relevant properties used downstream
        /// to create storage clients.
        /// <seealso cref="Microsoft.Extensions.Azure.ClientFactory" />
        /// A storage service URI can be built using just the account name along with default
        /// parameters for Scheme and Endpoint Suffix.
        /// </summary>
        internal class StorageServiceUriOptions
        {
            private const string DefaultScheme = "https";
            private const string DefaultEndpointSuffix = "core.windows.net";

            /// <summary>
            /// Gets or sets the resource URI for blob storage. If this property is given explicitly, it will be
            /// honored over the AccountName property.
            /// </summary>
            public string BlobServiceUri { get; set; }

            /// <summary>
            /// Gets or sets the name of the storage account.
            /// </summary>
            public string AccountName { get; set; }

            /// <summary>
            /// Constructs the blob service URI from the properties in this class.
            /// First checks if BlobServiceUri is specified. If not, the AccountName is used
            /// to construct a blob service URI with https scheme and core.windows.net endpoint suffix.
            /// </summary>
            /// <returns>Service URI to Azure blob storage</returns>
            public Uri GetBlobServiceUri()
            {
                if (!string.IsNullOrEmpty(BlobServiceUri))
                {
                    return new Uri(BlobServiceUri);
                }
                else if (!string.IsNullOrEmpty(AccountName))
                {
                    var uri = string.Format(CultureInfo.InvariantCulture, "{0}://{1}.blob.{2}", DefaultScheme, AccountName, DefaultEndpointSuffix);
                    return new Uri(uri);
                }

                return default;
            }
        }
    }
}
