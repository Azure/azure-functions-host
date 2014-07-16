// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    // CloudBlobClients are flyweights; distinct references do not equate to distinct storage accounts.
    internal class CloudBlobClientComparer : IEqualityComparer<CloudBlobClient>
    {
        public bool Equals(CloudBlobClient x, CloudBlobClient y)
        {
            return x.Credentials.AccountName == y.Credentials.AccountName;
        }

        public int GetHashCode(CloudBlobClient obj)
        {
            return obj.Credentials.AccountName.GetHashCode();
        }
    }
}
