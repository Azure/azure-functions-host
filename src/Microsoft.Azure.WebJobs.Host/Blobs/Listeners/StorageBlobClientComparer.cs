// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    // IStorageBlobClients are flyweights; distinct references do not equate to distinct storage accounts.
    internal class StorageBlobClientComparer : IEqualityComparer<IStorageBlobClient>
    {
        public bool Equals(IStorageBlobClient x, IStorageBlobClient y)
        {
            if (x == null)
            {
                throw new ArgumentNullException("x");
            }
            if (y == null)
            {
                throw new ArgumentNullException("y");
            }

            return x.Credentials.AccountName == y.Credentials.AccountName;
        }

        public int GetHashCode(IStorageBlobClient obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }

            return obj.Credentials.AccountName.GetHashCode();
        }
    }
}
