// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Azure.WebJobs.Script.StorageProvider
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// Extension methods for Storage Blobs integration.
    /// </summary>
    internal static class AzureBlobBuilderExtensions
    {
        /// <summary>
        /// Adds the core services needed to create Azure Blob clients using the BlobServiceClientProvider.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
        public static void AddAzureStorageBlobs(this IServiceCollection services)
        {
            services.AddAzureClientsCore();
            services.TryAddSingleton<BlobServiceClientProvider>();
        }
    }
}
