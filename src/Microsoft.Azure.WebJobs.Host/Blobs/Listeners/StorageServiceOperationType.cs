// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    /// <summary>
    /// Enumerates the operations that are logged for the corresponding storage service.
    /// See full list of possible operations at http://msdn.microsoft.com/en-us/library/windowsazure/hh343260.aspx.
    /// </summary>
    internal enum StorageServiceOperationType
    {
        AcquireLease,
        AcquireBlobLease,
        BreakLease,
        ClearPage,
        CopyBlob,
        CopyBlobSource,
        CopyBlobDestination,
        CreateContainer,
        DeleteBlob,
        DeleteContainer,
        GetBlob,
        GetBlobMetadata,
        GetBlobProperties,
        GetBlockList,
        GetContainerACL,
        GetContainerMetadata,
        GetContainerProperties,
        GetLeaseInfo,
        GetPageRegions,
        LeaseBlob,
        ListBlobs,
        ListContainers,
        PutBlob,
        PutBlockList,
        PutBlock,
        PutPage,
        ReleaseLease, 
        ReleaseBlobLease,
        RenewLease, 
        RenewBlobLease,
        SetBlobMetadata,
        SetBlobProperties,
        SetContainerACL,
        SetContainerMetadata,
        SnapshotBlob,
        SetBlobServiceProperties,
        GetBlobServiceProperties,
    }
}
