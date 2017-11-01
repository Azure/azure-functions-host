// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    [Collection("Azure Test Collection")]
    public class AppServiceWorkerTableTests
    {
        [Theory, MemberData(nameof(TestStorageConnectionString))]
        public async Task CRUDTests(string storageConnectionString, string siteName)
        {
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                return;
            }

            AppServiceSettings.StorageConnectionString = storageConnectionString;
            AppServiceSettings.SiteName = siteName;
            AppServiceSettings.WorkerName = "127.0.0.1";
            AppServiceSettings.HomeStampName = "waws-prod-stamp-001";
            AppServiceSettings.CurrentStampName = "waws-prod-stamp-001";

            var worker = new AppServiceWorkerInfo
            {
                PartitionKey = AppServiceSettings.WorkerPartitionKey,
                RowKey = AppServiceSettings.GetWorkerRowKey(AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName),
                StampName = AppServiceSettings.CurrentStampName,
                WorkerName = AppServiceSettings.WorkerName,
                LoadFactor = 55
            };

            try
            {
                var table = AppServiceWorkerTable.Instance;

                // intialize
                await DeleteAllWorkers(table);

                var workers = await table.List();
                Assert.Equal(0, workers.Count());

                // insert
                await table.AddOrUpdate(worker);

                workers = await table.List();
                Assert.Equal(1, workers.Count());
                var entity1 = workers.FirstOrDefault();
                Assert.True(ScaleUtils.WorkerEquals(entity1, worker));
                Assert.True(DateTime.UtcNow >= entity1.LastModifiedTimeUtc);
                Assert.True(DateTime.UtcNow - entity1.LastModifiedTimeUtc <= TimeSpan.FromSeconds(30));
                Assert.Equal(worker.LoadFactor, entity1.LoadFactor);
                Assert.False(entity1.IsStale);
                Assert.Equal(worker.IsHomeStamp, entity1.IsHomeStamp);

                // update
                worker.LoadFactor = 45;
                await table.AddOrUpdate(worker);

                workers = await table.List();
                Assert.Equal(1, workers.Count());
                var entity2 = workers.FirstOrDefault();
                Assert.True(ScaleUtils.WorkerEquals(entity1, entity2));
                Assert.True(entity2.LastModifiedTimeUtc > entity1.LastModifiedTimeUtc);
                Assert.Equal(worker.LoadFactor, entity2.LoadFactor);
                Assert.NotEqual(entity1.LoadFactor, entity2.LoadFactor);

                // delete
                await table.Delete(worker);
                workers = await table.List();
                Assert.Equal(0, workers.Count());
            }
            finally
            {
                ResetEnvironment();
            }
        }

        [Theory, MemberData(nameof(TestStorageConnectionString))]
        public async Task GetSetManagerTests(string storageConnectionString, string siteName)
        {
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                return;
            }

            AppServiceSettings.StorageConnectionString = storageConnectionString;
            AppServiceSettings.SiteName = siteName;
            AppServiceSettings.WorkerName = "127.0.0.1";
            AppServiceSettings.HomeStampName = "waws-prod-stamp-001";
            AppServiceSettings.CurrentStampName = "waws-prod-stamp-001";

            var worker = new AppServiceWorkerInfo
            {
                PartitionKey = AppServiceSettings.WorkerPartitionKey,
                RowKey = AppServiceSettings.GetWorkerRowKey(AppServiceSettings.CurrentStampName, AppServiceSettings.WorkerName),
                StampName = AppServiceSettings.CurrentStampName,
                WorkerName = AppServiceSettings.WorkerName,
                LoadFactor = 55
            };

            try
            {
                var table = AppServiceWorkerTable.Instance;

                // intialize
                await DeleteAllWorkers(table);

                var current = await table.GetManager();
                Assert.Null(current);

                // set manager
                await table.AddOrUpdate(worker);
                await table.SetManager(worker);

                current = await table.GetManager();
                Assert.True(ScaleUtils.WorkerEquals(worker, current));

                // delete
                await table.Delete(worker);
                current = await table.GetManager();
                Assert.Null(current);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        [Theory, MemberData(nameof(TestStorageConnectionString))]
        public async Task LeaseBasicTests(string storageConnectionString, string siteName)
        {
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                return;
            }

            AppServiceSettings.StorageConnectionString = storageConnectionString;
            AppServiceSettings.SiteName = siteName;
            AppServiceSettings.WorkerName = "127.0.0.1";
            try
            {
                var table = AppServiceWorkerTable.Instance;
                ILockHandle tableLock = null;
                try
                {
                    tableLock = await table.AcquireLock();
                    await tableLock.Release();
                    tableLock = null;

                    tableLock = await table.AcquireLock();
                    await tableLock.Release();
                    tableLock = null;
                }
                finally
                {
                    if (tableLock != null)
                    {
                        await tableLock.Release();
                    }
                }

                Assert.Null(tableLock);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        [Theory, MemberData(nameof(TestStorageConnectionString))]
        public async Task LeaseConflictTests(string storageConnectionString, string siteName)
        {
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                return;
            }

            AppServiceSettings.StorageConnectionString = storageConnectionString;
            AppServiceSettings.SiteName = siteName;
            AppServiceSettings.WorkerName = "127.0.0.1";
            try
            {
                var table = AppServiceWorkerTable.Instance;
                ILockHandle tableLock = null;
                try
                {
                    tableLock = await table.AcquireLock();
                    var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await table.AcquireLock());

                    Assert.Contains("Conflict", exception.Message);

                    await tableLock.Release();
                    tableLock = null;
                }
                finally
                {
                    if (tableLock != null)
                    {
                        await tableLock.Release();
                    }
                }

                Assert.Null(tableLock);
            }
            finally
            {
                ResetEnvironment();
            }
        }

        public static IEnumerable<object[]> TestStorageConnectionString
        {
            get
            {
                yield return new[] { Environment.GetEnvironmentVariable("SCALING_STORAGECONNECTIONSTRING"), Environment.MachineName.ToLower() };
            }
        }

        private async Task DeleteAllWorkers(IWorkerTable table)
        {
            var workers = await table.List();
            foreach (var worker in workers)
            {
                await table.Delete(worker);
            }
        }

        private void ResetEnvironment()
        {
            AppServiceSettings.StorageConnectionString = null;
            AppServiceSettings.SiteName = null;
            AppServiceSettings.WorkerName = null;
            AppServiceSettings.HomeStampName = null;
            AppServiceSettings.CurrentStampName = null;
        }
    }
}