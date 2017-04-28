// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class MockWorkerTable : Dictionary<string, IWorkerInfo>, IWorkerTable
    {
        private readonly MockWorkerTableLock _lock;
        private IWorkerInfo _manager;

        public MockWorkerTable()
        {
            _lock = new MockWorkerTableLock();
        }

        public virtual bool Locked
        {
            get { return !string.IsNullOrEmpty(_lock.Id); }
        }

        public virtual async Task<ILockHandle> AcquireLock()
        {
            return await _lock.AcquireLock();
        }

        public virtual Task AddOrUpdate(IWorkerInfo worker)
        {
            var workerInfo = (MockWorkerInfo)worker;
            this[GetWorkerKey(worker)] = workerInfo;
            workerInfo.LastModifiedTimeUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public virtual Task Delete(IWorkerInfo worker)
        {
            Remove(GetWorkerKey(worker));
            return Task.CompletedTask;
        }

        public virtual Task<IEnumerable<IWorkerInfo>> List()
        {
            return Task.FromResult((IEnumerable<IWorkerInfo>)Values);
        }

        public virtual Task<IWorkerInfo> GetManager()
        {
            return Task.FromResult(_manager);
        }

        public virtual Task SetManager(IWorkerInfo manager)
        {
            _manager = manager;
            return Task.CompletedTask;
        }

        private static string GetWorkerKey(IWorkerInfo worker)
        {
            return string.Format("{0}:{1}:{2}", worker.SiteName, worker.StampName, worker.WorkerName);
        }
    }
}