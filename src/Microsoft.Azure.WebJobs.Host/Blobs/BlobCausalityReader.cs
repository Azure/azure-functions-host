// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class BlobCausalityReader : IBlobCausalityReader
    {
        private static readonly BlobCausalityReader _instance = new BlobCausalityReader();

        private BlobCausalityReader()
        {
        }

        public static BlobCausalityReader Instance
        {
            get { return _instance; }
        }

        public Task<Guid?> GetWriterAsync(IStorageBlob blob, CancellationToken cancellationToken)
        {
            return BlobCausalityManager.GetWriterAsync(blob, cancellationToken);
        }
    }
}
