// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// Taken from Microsoft.WebJobs.Host
    /// </summary>
    internal class InMemoryDistributedLockManager : IDistributedLockManager
    {
        private Dictionary<string, FakeLock> _locks = new Dictionary<string, FakeLock>();

        public Task<string> GetLockOwnerAsync(string account, string lockId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        public Task ReleaseLockAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            FakeLock x = (FakeLock)lockHandle;
            lock (_locks)
            {
                _locks.Remove(x.LockId);
            }
            return Task.CompletedTask;
        }

        public Task<bool> RenewAsync(IDistributedLock lockHandle, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<IDistributedLock> TryLockAsync(string account, string lockId, string lockOwnerId, string proposedLeaseId, TimeSpan lockPeriod, CancellationToken cancellationToken)
        {
            FakeLock entry = null;
            lock (_locks)
            {
                if (!_locks.ContainsKey(lockId))
                {
                    entry = new FakeLock
                    {
                        LockId = lockId,
                        LockOwnerId = lockOwnerId
                    };
                    _locks[lockId] = entry;
                }
            }
            return Task.FromResult<IDistributedLock>(entry);
        }

        private class FakeLock : IDistributedLock
        {
            public string LockId { get; set; }

            public string LockOwnerId { get; set; }

            public Task LeaseLost { get { throw new NotImplementedException(); } }
        }
    }
}
