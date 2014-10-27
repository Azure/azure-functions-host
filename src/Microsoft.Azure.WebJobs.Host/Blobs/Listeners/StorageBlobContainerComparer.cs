// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    // IStorageBlobContainers are flyweights; distinct references do not equate to distinct containers.
    internal class StorageBlobContainerComparer : IEqualityComparer<IStorageBlobContainer>
    {
        public bool Equals(IStorageBlobContainer x, IStorageBlobContainer y)
        {
            return x.Uri == y.Uri;
        }

        public int GetHashCode(IStorageBlobContainer obj)
        {
            return obj.Uri.GetHashCode();
        }
    }
}
