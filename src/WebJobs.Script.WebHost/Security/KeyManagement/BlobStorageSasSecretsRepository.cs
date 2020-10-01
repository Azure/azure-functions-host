// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// An <see cref="BlobStorageSecretsRepository"/> implementation that uses a SAS connection string to connect to Azure blob storage.
    /// </summary>
    public sealed class BlobStorageSasSecretsRepository : BlobStorageSecretsRepository
    {
        public BlobStorageSasSecretsRepository(string secretSentinelDirectoryPath, string containerSasUri, string siteSlotName, ILogger logger, IEnvironment environment)
            : base(secretSentinelDirectoryPath, containerSasUri, siteSlotName, logger, environment)
        {
        }

        protected override CloudBlobContainer CreateBlobContainer(string containerSasUri)
        {
            return new CloudBlobContainer(new Uri(containerSasUri));
        }

        protected override void LogErrorMessage(string operation)
        {
            Logger?.BlobStorageSecretSasRepoError(operation, EnvironmentSettingNames.AzureWebJobsSecretStorageSas);
        }
    }
}
