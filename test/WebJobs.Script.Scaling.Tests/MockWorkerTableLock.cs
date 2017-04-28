// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class MockWorkerTableLock : ILockHandle
    {
        private bool _locked;

        public MockWorkerTableLock()
        {
        }

        public void Dispose()
        {
            if (_locked)
            {
                Release().Wait();
            }
        }

        public virtual string Id { get; set; }

        public virtual Task<ILockHandle> AcquireLock()
        {
            if (_locked)
            {
                throw new InvalidOperationException("Lock is currently acquired!");
            }

            _locked = true;
            Id = Guid.NewGuid().ToString();
            return Task.FromResult<ILockHandle>(this);
        }

        public virtual Task Release()
        {
            if (!_locked)
            {
                throw new InvalidOperationException("Lock is not acquired!");
            }

            _locked = false;
            Id = null;
            return Task.CompletedTask;
        }
    }
}