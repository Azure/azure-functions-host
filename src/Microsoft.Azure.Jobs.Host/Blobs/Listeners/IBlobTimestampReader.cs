// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal interface IBlobTimestampReader
    {
        Task<DateTime?> GetLastModifiedTimestampAsync(ICloudBlob blob, CancellationToken cancellationToken);
    }
}
