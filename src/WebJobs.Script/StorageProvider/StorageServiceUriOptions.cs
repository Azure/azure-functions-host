// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Script
{
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
        /// Gets or sets the resource URI for table storage. If this property is given explicitly, it will be
        /// honored over the AccountName property.
        /// </summary>
        public Uri TableServiceUri { get; set; }

        /// <summary>
        /// Gets or sets the name of the storage account.
        /// </summary>
        public string AccountName { get; set; }

        /// <summary>
        /// Constructs the blob service URI from the properties in this class.
        /// First checks if BlobServiceUri is specified. If not, the AccountName is used
        /// to construct a blob service URI with https scheme and core.windows.net endpoint suffix.
        /// </summary>
        /// <returns>Service URI to Azure blob storage.</returns>
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

        /// <summary>
        /// Constructs the table service URI from the properties in this class.
        /// First checks if TableServiceUri is specified. If not, the AccountName is used
        /// to construct a table service URI with https scheme and core.windows.net endpoint suffix.
        /// </summary>
        /// <returns>Service URI to Azure table storage.</returns>
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
