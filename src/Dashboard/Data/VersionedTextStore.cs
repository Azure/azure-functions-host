// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class VersionedTextStore
    {
        [CLSCompliant(false)]
        public static IVersionedMetadataTextStore CreateBlobStore(CloudBlobClient client, string containerName,
            string directoryName)
        {
            IConcurrentMetadataTextStore innerStore = ConcurrentTextStore.CreateBlobStore(client, containerName,
                directoryName);
            IVersionMetadataMapper versionMapper = VersionMetadataMapper.Instance;
            return new VersionedMetadataTextStore(innerStore, versionMapper);
        }
    }
}
