// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="BlobStorageSecretsRepository"/> implementation that uses a SAS connection string to connect to Azure blob storage.
    /// </summary>
    public sealed class BlobStorageSasSecretsRepository : BlobStorageSecretsRepository
    {
        public BlobStorageSasSecretsRepository(string secretSentinelDirectoryPath, string containerSasUri, string siteSlotName, ILogger logger, IEnvironment environment, IAzureStorageProvider azureStorageProvider)
            : base(secretSentinelDirectoryPath, containerSasUri, siteSlotName, logger, environment, azureStorageProvider)
        {
        }

        public override string Name => nameof(BlobStorageSasSecretsRepository);

        protected override BlobContainerClient CreateBlobContainerClient(string containerSasUri)
        {
            return new BlobContainerClient(new Uri(containerSasUri));
        }

        protected override void LogErrorMessage(string operation)
        {
            Logger?.BlobStorageSecretSasRepoError(operation, EnvironmentSettingNames.AzureWebJobsSecretStorageSas);
        }
    }
}
