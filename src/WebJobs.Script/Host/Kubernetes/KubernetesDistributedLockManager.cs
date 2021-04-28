// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    // This is an implementation of IDistributedLockManager to be used when running
    // in Kubernetes environments.
    internal class KubernetesDistributedLockManager : IDistributedLockManager
    {
        private readonly ILogger _logger;
        private readonly KubernetesClient _kubernetesClient;

        internal KubernetesDistributedLockManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
            _kubernetesClient = new KubernetesClient();
        }

        public async Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            var response = await _kubernetesClient.GetLock(lockId);
            return response.Owner;
        }

        public async Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            var kubernetesLock = (KubernetesLockHandle)lockHandle;
            var response = await _kubernetesClient.ReleaseLock(kubernetesLock.LockId, kubernetesLock.OwnerId);
            response.EnsureSuccessStatusCode();
        }

        public async Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            var kubernetesLock = (KubernetesLockHandle)lockHandle;
            await _kubernetesClient.TryAcquireLock(kubernetesLock.LockId, kubernetesLock.OwnerId, kubernetesLock.LockPeriod);
            return true;
        }

        public async Task<IDistributedLock> TryLockAsync(string account, string lockId, string lockOwnerId, string proposedLeaseId, TimeSpan lockPeriod, CancellationToken cancellationToken)
        {
            var kubernetesLock = await _kubernetesClient.TryAcquireLock(lockId, lockOwnerId, lockPeriod.ToString());
            if (string.IsNullOrEmpty(kubernetesLock.LockId))
            {
                return null;
            }
            return kubernetesLock;
        }
    }
}
