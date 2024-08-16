// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
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
            // If connection string is present, it will be honored first; bypass creating a serviceUri
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
            /// Gets or sets the resource URI for table storage. If this property is given explicitly, it will be
            /// honored over the AccountName property.
            /// </summary>
            public Uri TableServiceUri { get; set; }

            /// <summary>
            /// Gets or sets the name of the storage account.
            /// </summary>
            public string AccountName { get; set; }

            /// <summary>
            /// Constructs the table service URI from the properties in this class.
            /// First checks if TableServiceUri is specified. If not, the AccountName is used
            /// to construct a table service URI with https scheme and core.windows.net endpoint suffix.
            /// </summary>
            /// <returns>Service URI to Azure Table storage.</returns>
            public Uri GetTableServiceUri()
            {
                if (TableServiceUri is not null)
                {
                    return TableServiceUri;
                }

                if (!string.IsNullOrEmpty(AccountName))
                {
                    var uri = string.Format(CultureInfo.InvariantCulture, "{0}://{1}.table.{2}", DefaultScheme, AccountName, DefaultEndpointSuffix);
                    return new Uri(uri);
                }

                return default;
            }
        }
    }
}
