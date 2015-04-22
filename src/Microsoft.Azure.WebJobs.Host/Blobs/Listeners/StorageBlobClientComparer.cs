// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    // IStorageBlobClients are flyweights; distinct references do not equate to distinct storage accounts.
    internal class StorageBlobClientComparer : IEqualityComparer<IStorageBlobClient>
    {
        public bool Equals(IStorageBlobClient x, IStorageBlobClient y)
        {
            return x.Credentials.AccountName == y.Credentials.AccountName;
        }

        public int GetHashCode(IStorageBlobClient obj)
        {
            return obj.Credentials.AccountName.GetHashCode();
        }
    }
}
