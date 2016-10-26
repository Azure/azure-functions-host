// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Storage
{
    internal enum StorageAccountType
    {
        // Supports blob/table/queue.
        // Can be used as primary or secondary storage.
        GeneralPurpose,

        // Supports only blobs.
        // Cannot be used as primary storage account (AzureWebJobsStorage).
        BlobOnly,

        // Supports only page blobs, no logging.
        // Cannot be used as primary storage account (AzureWebJobsStorage).
        // Cannot be used as blob trigger due to lack of logs.
        Premium
    }
}
