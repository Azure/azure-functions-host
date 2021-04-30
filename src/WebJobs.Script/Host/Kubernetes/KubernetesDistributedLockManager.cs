// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script
{
    // This is an implementation of IDistributedLockManager to be used when running
    // in Kubernetes environments.
    internal class KubernetesDistributedLockManager : IDistributedLockManager
    {
        private readonly KubernetesClient _kubernetesClient;
        private readonly string _websiteInstanceId;

        public KubernetesDistributedLockManager(IEnvironment environment,
            ScriptSettingsManager settingsManager)
            : this(new KubernetesClient(environment), settingsManager)
        {
        }

        internal KubernetesDistributedLockManager(KubernetesClient client, ScriptSettingsManager settingsManager)
        {
            _kubernetesClient = client;
            _websiteInstanceId = settingsManager.AzureWebsiteInstanceId;
        }

        public async Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            var response = await _kubernetesClient.GetLock(lockId, cancellationToken);
            return response.Owner;
        }

        public async Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            var kubernetesLock = (KubernetesLockHandle)lockHandle;
            var response = await _kubernetesClient.ReleaseLock(kubernetesLock.LockId, kubernetesLock.Owner);
            response.EnsureSuccessStatusCode();
        }

        public async Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            var kubernetesLock = (KubernetesLockHandle)lockHandle;
            var renewedLockHandle = await _kubernetesClient.TryAcquireLock(kubernetesLock.LockId, kubernetesLock.Owner, TimeSpan.Parse(kubernetesLock.LockPeriod), cancellationToken);
            return !string.IsNullOrEmpty(renewedLockHandle.LockId);
        }

        public async Task<IDistributedLock> TryLockAsync(string account, string lockId, string lockOwnerId, string proposedLeaseId, TimeSpan lockPeriod, CancellationToken cancellationToken)
        {
            var ownerId = string.IsNullOrEmpty(lockOwnerId) ? _websiteInstanceId : lockOwnerId;
            var kubernetesLock = await _kubernetesClient.TryAcquireLock(lockId, ownerId, lockPeriod, cancellationToken);
            if (string.IsNullOrEmpty(kubernetesLock.LockId))
            {
                return null;
            }
            return kubernetesLock;
        }
    }
}
