// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    // CloudBlobContainers are flyweights; distinct references do not equate to distinct containers.
    internal class CloudBlobContainerComparer : IEqualityComparer<CloudBlobContainer>
    {
        public bool Equals(CloudBlobContainer x, CloudBlobContainer y)
        {
            return x.Uri == y.Uri;
        }

        public int GetHashCode(CloudBlobContainer obj)
        {
            return obj.Uri.GetHashCode();
        }
    }
}
