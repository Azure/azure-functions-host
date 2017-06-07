// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.TestCommon;
using System;
using System.Threading;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Singleton
{
    public class SingletonEnd2EndTests 
    {
        [Fact]
        public async Task ValidateExclusion()
        {
            var core = new FakeSingletonManager();
            var host = TestHelpers.NewJobHost<Program>(core);

            var task1 = host.CallAsync("Func1", null);
            var task2 = host.CallAsync("Func1", null);
            
            await Task.WhenAll(task1, task2); 
        }

        // Ensure singletons are serialized 
        public class Program
        {
            // Ensure exclusion
            static int _counter = 0;

            [NoAutomaticTrigger]
            [Singleton] // should ensure all instances of Func1 are exclusive. 
            public async Task Func1()
            {
                var newVal = Interlocked.Increment(ref _counter);
                Assert.Equal(1, newVal);

                // Wait long enough that if singleton is not working, the other function would have started. 
                await Task.Delay(300);

                newVal = Interlocked.Decrement(ref _counter);
                Assert.Equal(0, newVal);
            }
        }


        internal class FakeSingletonManager : IDistributedLockManager
        {
            Dictionary<string, FakeLock> _locks = new Dictionary<string, FakeLock>();

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

            class FakeLock : IDistributedLock
            {
                public string LockId { get; set; }
                public string LockOwnerId { get; set; }

                public Task LeaseLost { get { throw new NotImplementedException(); } }
            }
        }
    }
}