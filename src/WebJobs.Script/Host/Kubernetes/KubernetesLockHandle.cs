// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class KubernetesLockHandle : IDistributedLock
    {
        public string LockId { get; set; }

        public string Owner { get; set; }

        public string LockPeriod { get; set; }
    }
}
