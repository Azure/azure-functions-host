// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Data.Tables;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides methods for creating Azure table storage clients, ensuring all necessary configuration is applied.
    /// Implementations are responsible instantiating these clients and using desired options, credentials, or service URIs.
    /// </summary>
    public interface IAzureTableStorageProvider
    {
        /// <summary>
        /// Attempts to create the <see cref="TableServiceClient"/> used for internal storage.
        /// </summary>
        /// <param name="client"><see cref="TableServiceClient"/> to instantiate.</param>
        /// <returns>Whether the attempt was successful.</returns>
        bool TryCreateHostingTableServiceClient(out TableServiceClient client);

        /// <summary>
        /// Attempts to create the <see cref="TableServiceClient"/> from the specified connection.
        /// </summary>
        /// <param name="connection">connection name to use.</param>
        /// <param name="client"><see cref="TableServiceClient"/> to instantiate.</param>
        /// <returns>Whether the attempt was successful.</returns>
        bool TryCreateTableServiceClient(string connection, out TableServiceClient client);
    }
}