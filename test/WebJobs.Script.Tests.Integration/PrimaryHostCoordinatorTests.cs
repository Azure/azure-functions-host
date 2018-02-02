// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PrimaryHostCoordinatorTests
    {
        // Helper to get a real Blob Lease based IDistributedLockManager
        private static IDistributedLockManager CreateLockManager()
        {
            JobHostConfiguration config = new JobHostConfiguration();
            var host = new JobHost(config);
            var lockManager = (IDistributedLockManager)host.Services.GetService(typeof(IDistributedLockManager));
            return lockManager;
        }

        [Fact]
        public async Task HasLease_WhenLeaseIsAcquired_ReturnsTrue()
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose);

            using (var manager = PrimaryHostCoordinator.Create(CreateLockManager(), TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter, null))
            {
                await TestHelpers.Await(() => manager.HasLease);
            }

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public async Task HasLeaseChanged_WhenLeaseIsAcquiredAndStateChanges_IsFired()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var resetEvent = new ManualResetEventSlim();

            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            // Acquire a lease on the host lock blob
            string leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(15));

            PrimaryHostCoordinator manager = null;

            try
            {
                manager = PrimaryHostCoordinator.Create(CreateLockManager(), TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter, null);
                manager.HasLeaseChanged += (s, a) => resetEvent.Set();
            }
            finally
            {
                await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = leaseId });
            }

            resetEvent.Wait(TimeSpan.FromSeconds(15));
            bool hasLease = manager.HasLease;

            manager.Dispose();

            Assert.True(resetEvent.IsSet);
            Assert.True(hasLease, $"{nameof(PrimaryHostCoordinator.HasLease)} was not correctly set to 'true' when lease was acquired.");

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public async Task HasLeaseChanged_WhenLeaseIsLostAndStateChanges_IsFired()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var resetEvent = new ManualResetEventSlim();

            PrimaryHostCoordinator manager = null;
            string tempLeaseId = null;

            var lockManager = CreateLockManager();
            var renewalInterval = TimeSpan.FromSeconds(3);
            using (manager = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter, null, renewalInterval))
            {
                try
                {
                    await TestHelpers.Await(() => manager.HasLease);

                    manager.HasLeaseChanged += (s, a) => resetEvent.Set();

                    // Release the manager's lease and acquire one with a different id
                    await lockManager.ReleaseLockAsync(manager.LockHandle, CancellationToken.None);
                    tempLeaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(30), Guid.NewGuid().ToString());
                }
                finally
                {
                    if (tempLeaseId != null)
                    {
                        await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = tempLeaseId });
                    }
                }

                resetEvent.Wait(TimeSpan.FromSeconds(15));
            }

            Assert.True(resetEvent.IsSet);
            Assert.False(manager.HasLease, $"{nameof(PrimaryHostCoordinator.HasLease)} was not correctly set to 'false' when lease lost.");

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public async Task Dispose_ReleasesBlobLease()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);

            var traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose);

            var lockManager = CreateLockManager();
            using (var manager = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter, null))
            {
                await TestHelpers.Await(() => manager.HasLease);
            }

            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            string leaseId = null;
            try
            {
                // Acquire a lease on the host lock blob
                leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(15));

                await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = leaseId });
            }
            catch (StorageException exc) when (exc.RequestInformation.HttpStatusCode == 409)
            {
            }

            Assert.False(string.IsNullOrEmpty(leaseId), "Failed to acquire a blob lease. The lease was not properly released.");

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseIsAcquired()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var renewResetEvent = new ManualResetEventSlim();

            var blobMock = new Mock<IDistributedLockManager>();
            blobMock.Setup(b => b.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IDistributedLock>(new FakeLock()));

            using (var manager = new PrimaryHostCoordinator(blobMock.Object, TimeSpan.FromSeconds(5), hostId, instanceId, traceWriter, null))
            {
                renewResetEvent.Wait(TimeSpan.FromSeconds(10));

                // Make sure we have enough time to trace the renewal
                await TestHelpers.Await(() => traceWriter.GetTraces().Count == 1, 5000, 500);
            }

            TraceEvent acquisitionEvent = traceWriter.GetTraces().First();
            Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.Message);
            Assert.Equal(TraceLevel.Info, acquisitionEvent.Level);
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseRenewalFails()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var renewResetEvent = new ManualResetEventSlim();

            var blobMock = new Mock<IDistributedLockManager>();
            blobMock.Setup(b => b.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IDistributedLock>(new FakeLock()));

            blobMock.Setup(b => b.RenewAsync(It.IsAny<IDistributedLock>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromException<bool>(new StorageException(new RequestResult { HttpStatusCode = 409 }, "test", null)))
                .Callback(() => renewResetEvent.Set());

            using (var manager = new PrimaryHostCoordinator(blobMock.Object, TimeSpan.FromSeconds(5), hostId, instanceId, traceWriter, null))
            {
                renewResetEvent.Wait(TimeSpan.FromSeconds(10));
                await TestHelpers.Await(() => traceWriter.GetTraces().Count == 2, 5000, 500);
            }

            TraceEvent acquisitionEvent = traceWriter.GetTraces().First();
            Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.Message);
            Assert.Equal(TraceLevel.Info, acquisitionEvent.Level);

            TraceEvent renewalEvent = traceWriter.GetTraces().Skip(1).First();
            string pattern = @"Failed to renew host lock lease: Another host has acquired the lease. The last successful renewal completed at (.+) \([0-9]+ milliseconds ago\) with a duration of [0-9]+ milliseconds.";
            Assert.True(Regex.IsMatch(renewalEvent.Message, pattern), $"Expected trace event {pattern} not found.");
            Assert.Equal(TraceLevel.Info, renewalEvent.Level);
        }

        [Fact]
        public async Task DifferentHosts_UsingSameStorageAccount_CanObtainLease()
        {
            string hostId1 = Guid.NewGuid().ToString();
            string hostId2 = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            var lockManager = CreateLockManager();
            using (var manager1 = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), hostId1, instanceId, traceWriter, null))
            using (var manager2 = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), hostId2, instanceId, traceWriter, null))
            {
                Task manager1Check = TestHelpers.Await(() => manager1.HasLease);
                Task manager2Check = TestHelpers.Await(() => manager2.HasLease);

                await Task.WhenAll(manager1Check, manager2Check);
            }

            await Task.WhenAll(ClearLeaseBlob(hostId1), ClearLeaseBlob(hostId2));
        }

        private static async Task<ICloudBlob> GetLockBlobAsync(string accountConnectionString, string hostId)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
            CloudBlobClient client = account.CreateCloudBlobClient();

            var container = client.GetContainerReference(PrimaryHostCoordinator.HostContainerName);

            await container.CreateIfNotExistsAsync();
            CloudBlockBlob blob = container.GetBlockBlobReference(PrimaryHostCoordinator.GetBlobName(hostId));
            if (!await blob.ExistsAsync())
            {
                await blob.UploadFromStreamAsync(new MemoryStream());
            }

            return blob;
        }

        private async Task ClearLeaseBlob(string hostId)
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            try
            {
                await blob.BreakLeaseAsync(TimeSpan.Zero);
            }
            catch
            {
            }

            await blob.DeleteIfExistsAsync();
        }

        private class FakeLock : IDistributedLock
        {
            public string LockId => "lockid";

            public Task LeaseLost => throw new NotImplementedException();
        }
    }
}
