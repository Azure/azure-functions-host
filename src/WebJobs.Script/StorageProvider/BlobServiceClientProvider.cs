// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Pass-through class to create a <see cref="BlobServiceClient"/> using <see cref="AzureComponentFactory"/>.
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

        /// <inheritdoc/>
        protected override string ServiceUriSubDomain
        {
            get
            {
                return "blob";
            }
        }

        protected override BlobServiceClient CreateClient(IConfiguration configuration, TokenCredential tokenCredential, BlobClientOptions options)
        {
            // If connection string is present, it will be honored first; bypass creating a serviceUri
            if (!IsConnectionStringPresent(configuration))
            {
                var serviceUri = configuration.Get<StorageServiceUriOptions>().GetServiceUri(ServiceUriSubDomain);
                if (serviceUri != null)
                {
                    return new BlobServiceClient(serviceUri, tokenCredential, options);
                }
            }

            return base.CreateClient(configuration, tokenCredential, options);
        }
    }
}
