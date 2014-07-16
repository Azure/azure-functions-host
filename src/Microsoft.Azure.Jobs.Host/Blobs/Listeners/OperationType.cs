// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    // See list at http://msdn.microsoft.com/en-us/library/windowsazure/hh343260.aspx
    internal enum OperationType
    {
        AcquireLease,
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
        RenewLease,
        SetBlobMetadata,
        SetBlobProperties,
        SetContainerACL,
        SetContainerMetadata,
        SnapshotBlob,
        SetBlobServiceProperties,
        GetBlobServiceProperties,
    }
}
