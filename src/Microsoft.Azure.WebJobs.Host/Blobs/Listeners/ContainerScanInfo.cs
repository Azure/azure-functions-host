// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class ContainerScanInfo
    {
        public ICollection<ITriggerExecutor<IStorageBlob>> Registrations { get; set; }

        public DateTime LastSweepCycleStartTime { get; set; }

        public DateTime CurrentSweepCycleStartTime { get; set; }

        public BlobContinuationToken ContinuationToken { get; set; }
    }
}
