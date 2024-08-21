// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Pass-through class to create a <see cref="TableServiceClient"/> using <see cref="AzureComponentFactory"/>.
    /// This class uses additional configuration settings for a specified connection in addition to those in <see cref="AzureComponentFactory"/>.
    /// To support scenarios where a storage connection (i.e. AzureWebJobsStorage) needs to reference multiple serviceUris (blob and queue),
    /// properties in <see cref="StorageServiceUriOptions"/> are supported to create the Azure table service URI.
    /// </summary>
    internal sealed class TableServiceClientProvider : StorageClientProvider<TableServiceClient, TableClientOptions>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TableServiceClientProvider"/> class that uses the registered Azure services to create a <see cref="TableServiceClient"/>.
        /// </summary>
        /// <param name="componentFactory">The Azure factory responsible for creating clients. <see cref="AzureComponentFactory"/>.</param>
        /// <param name="logForwarder">Log forwarder that forwards events to ILogger. <see cref="AzureEventSourceLogForwarder"/>.</param>
        public TableServiceClientProvider(AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder) : base(componentFactory, logForwarder)
        {
        }

        protected override TableServiceClient CreateClient(IConfiguration configuration, TokenCredential tokenCredential, TableClientOptions options)
        {
            // Resolve the service URI if the connection string is not defined
            if (!IsConnectionStringPresent(configuration))
            {
                var serviceUri = configuration.Get<StorageServiceUriOptions>()?.GetTableServiceUri();
                if (serviceUri != null)
                {
                    return new TableServiceClient(serviceUri, tokenCredential, options);
                }
            }

            return base.CreateClient(configuration, tokenCredential, options);
        }
    }
}
