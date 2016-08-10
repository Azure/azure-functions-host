// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class BlobLeaseManagerTests
    {
        [Fact]
        [Trait("Category", "E2E")]
        public async Task HasLease_WhenLeaseIsAcquired_ReturnsTrue()
        {
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose);

            using (var manager = await BlobLeaseManager.CreateAsync(connectionString, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter))
            {
                await TestHelpers.Await(() => manager.HasLease);

                Assert.Equal(instanceId, manager.LeaseId);
            }

            await ClearLeaseBlob(hostId);
        }
        
        [Fact]
        [Trait("Category", "E2E")]
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

            BlobLeaseManager manager = null;

            try
            {
                manager = await BlobLeaseManager.CreateAsync(connectionString, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter);
                manager.HasLeaseChanged += (s, a) => resetEvent.Set();
            }
            finally
            {
                await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = leaseId });
            }

            resetEvent.Wait(TimeSpan.FromSeconds(15));

            manager.Dispose();

            Assert.True(resetEvent.IsSet);
            Assert.True(manager.HasLease, $"{nameof(BlobLeaseManager.HasLease)} was not correctly set to 'true' when lease was acquired.");
            Assert.Equal(instanceId, manager.LeaseId);

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        [Trait("Category", "E2E")]
        public async Task HasLeaseChanged_WhenLeaseIsLostAndStateChanges_IsFired()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);
            
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var resetEvent = new ManualResetEventSlim();

            BlobLeaseManager manager = null;
            string tempLeaseId = null;

            using (manager = new BlobLeaseManager(blob, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter, TimeSpan.FromSeconds(3)))
            {
                try
                {
                    await TestHelpers.Await(() => manager.HasLease);

                    manager.HasLeaseChanged += (s, a) => resetEvent.Set();

                    // Release the manager's lease and acquire one with a different id
                    await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = manager.LeaseId });
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
            Assert.False(manager.HasLease, $"{nameof(BlobLeaseManager.HasLease)} was not correctly set to 'false' when lease lost.");

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public void AcquiringLease_WithServerError_LogsAndRetries()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var renewResetEvent = new ManualResetEventSlim();

            var results = new Queue<Task<string>>();
            results.Enqueue(Task.FromException<string>(new StorageException(new RequestResult { HttpStatusCode = 500 }, "test", null)));
            results.Enqueue(Task.FromResult(hostId));
            
            var blobMock = new Mock<ICloudBlob>();
            blobMock.Setup(b => b.AcquireLeaseAsync(It.IsAny<TimeSpan>(), It.IsAny<string>()))
                .Returns(() => results.Dequeue());

            blobMock.Setup(b => b.RenewLeaseAsync(It.IsAny<AccessCondition>()))
                .Returns(() => Task.Delay(1000))
                .Callback(() => renewResetEvent.Set());

            BlobLeaseManager manager;
            using (manager = new BlobLeaseManager(blobMock.Object, TimeSpan.FromSeconds(5), hostId, instanceId, traceWriter))
            {
                renewResetEvent.Wait(TimeSpan.FromSeconds(10));
            }

            Assert.True(renewResetEvent.IsSet);
            Assert.True(manager.HasLease);
            Assert.True(traceWriter.Traces.Any(t => t.Message.Contains("Server error")));
        }

        [Fact]
        [Trait("Category", "E2E")]
        public async Task Renew_WhenBlobIsDeleted_RecreatesBlob()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var renewResetEvent = new ManualResetEventSlim();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);

            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            var blobMock = new Mock<ICloudBlob>();
            blobMock.Setup(b => b.AcquireLeaseAsync(It.IsAny<TimeSpan>(), It.IsAny<string>()))
                .Returns(() => Task.FromResult(hostId));

            blobMock.Setup(b => b.RenewLeaseAsync(It.IsAny<AccessCondition>()))
                .Returns(() => Task.FromException<string>(new StorageException(new RequestResult { HttpStatusCode = 404 }, "test", null)))
                .Callback(() => Task.Delay(1000).ContinueWith(t => renewResetEvent.Set()));

            blobMock.SetupGet(b => b.ServiceClient).Returns(blob.ServiceClient);

            // Delete the blob
            await blob.DeleteIfExistsAsync();

            using (var manager = new BlobLeaseManager(blobMock.Object, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter, TimeSpan.FromSeconds(3)))
            {
                renewResetEvent.Wait(TimeSpan.FromSeconds(10));

                await TestHelpers.Await(() => manager.HasLease);
            }

            bool blobExists = await blob.ExistsAsync();

            Assert.True(renewResetEvent.IsSet);
            Assert.True(blobExists);

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        [Trait("Category", "E2E")]
        public async Task Dispose_ReleasesBlobLease()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            
            var traceWriter = new TestTraceWriter(System.Diagnostics.TraceLevel.Verbose);

            using (var manager = await BlobLeaseManager.CreateAsync(connectionString, TimeSpan.FromSeconds(15), hostId, instanceId, traceWriter))
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

            var blobMock = new Mock<ICloudBlob>();
            blobMock.Setup(b => b.AcquireLeaseAsync(It.IsAny<TimeSpan>(), It.IsAny<string>()))
                .Returns(() => Task.FromResult(hostId));

            blobMock.Setup(b => b.RenewLeaseAsync(It.IsAny<AccessCondition>()))
                .Returns(() => Task.Delay(10).ContinueWith(t => renewResetEvent.Set()));

            using (var manager = new BlobLeaseManager(blobMock.Object, TimeSpan.FromSeconds(5), hostId, instanceId, traceWriter))
            {
                renewResetEvent.Wait(TimeSpan.FromSeconds(10));

                // Make sure we have enough time to trace the renewal
                await TestHelpers.Await(() => traceWriter.Traces.Count == 2, 5000, 500);
            }

            TraceEvent acquisitionEvent = traceWriter.Traces.First();
            Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.Message);
            Assert.Equal(TraceLevel.Info, acquisitionEvent.Level);

            TraceEvent renewalEvent = traceWriter.Traces.Skip(1).First();
            Assert.Contains("Host lock lease renewed.", renewalEvent.Message);
            Assert.Equal(TraceLevel.Verbose, renewalEvent.Level);
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseAcquisitionFails()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var acquisitionResetEvent = new ManualResetEventSlim();

            var blobMock = new Mock<ICloudBlob>();
            blobMock.Setup(b => b.AcquireLeaseAsync(It.IsAny<TimeSpan>(), It.IsAny<string>()))
                .Returns(() => Task.FromException<string>(new StorageException(new RequestResult { HttpStatusCode = 409 }, "test", null)))
                .Callback(() => acquisitionResetEvent.Set());

            using (var manager = new BlobLeaseManager(blobMock.Object, TimeSpan.FromSeconds(10), hostId, instanceId, traceWriter))
            {
                acquisitionResetEvent.Wait(TimeSpan.FromSeconds(5));
            }

            // Make sure we have enough time to trace the renewal
            await TestHelpers.Await(() => traceWriter.Traces.Count == 1, 5000, 500);

            TraceEvent acquisitionEvent = traceWriter.Traces.First();
            Assert.Contains($"Host instance '{instanceId}' failed to acquire host lock lease: Another host has an active lease.", acquisitionEvent.Message);
            Assert.Equal(TraceLevel.Verbose, acquisitionEvent.Level);
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseRenewalFails()
        {
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var renewResetEvent = new ManualResetEventSlim();

            var blobMock = new Mock<ICloudBlob>();
            blobMock.Setup(b => b.AcquireLeaseAsync(It.IsAny<TimeSpan>(), It.IsAny<string>()))
                .Returns(() => Task.FromResult(hostId));

            blobMock.Setup(b => b.RenewLeaseAsync(It.IsAny<AccessCondition>()))
                .Returns(() => Task.FromException(new StorageException(new RequestResult { HttpStatusCode = 409 }, "test", null)))
                .Callback(() => renewResetEvent.Set());

            using (var manager = new BlobLeaseManager(blobMock.Object, TimeSpan.FromSeconds(5), hostId, instanceId, traceWriter))
            {
                renewResetEvent.Wait(TimeSpan.FromSeconds(10));
                // Make sure we have enough time to trace the renewal
                await TestHelpers.Await(() => traceWriter.Traces.Count == 2, 5000, 500);
            }

            TraceEvent acquisitionEvent = traceWriter.Traces.First();
            Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.Message);
            Assert.Equal(TraceLevel.Info, acquisitionEvent.Level);

            TraceEvent renewalEvent = traceWriter.Traces.Skip(1).First();
            Assert.Contains("Failed to renew host lock lease", renewalEvent.Message);
            Assert.Equal(TraceLevel.Info, renewalEvent.Level);
        }

        [Fact]
        [Trait("Category", "E2E")]
        public async Task DifferentHosts_UsingSameStorageAccount_CanObtainLease()
        {
            string hostId1 = Guid.NewGuid().ToString();
            string hostId2 = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            using (var manager1 = await BlobLeaseManager.CreateAsync(connectionString, TimeSpan.FromSeconds(15), hostId1, instanceId, traceWriter))
            using (var manager2 = await BlobLeaseManager.CreateAsync(connectionString, TimeSpan.FromSeconds(15), hostId2, instanceId, traceWriter))
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

            var container = client.GetContainerReference(BlobLeaseManager.HostContainerName);

            await container.CreateIfNotExistsAsync();
            CloudBlockBlob blob = container.GetBlockBlobReference(BlobLeaseManager.GetBlobName(hostId));
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
    }
}
