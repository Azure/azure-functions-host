// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PrimaryHostCoordinatorTests
    {
        private ILoggerFactory _loggerFactory = new LoggerFactory();
        private TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public PrimaryHostCoordinatorTests()
        {
            _loggerFactory.AddProvider(_loggerProvider);
        }

        private static IHost CreateHost()
        {
            var host = new HostBuilder()
                .ConfigureDefaultTestScriptHost(o => o.ScriptPath = Path.GetTempPath())
                .Build();

            return host;
        }

        [Theory]
        [InlineData(14.99)]
        [InlineData(60.01)]
        public void RejectsInvalidLeaseTimeout(double leaseTimeoutSeconds)
        {
            var leaseTimeout = TimeSpan.FromSeconds(leaseTimeoutSeconds);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PrimaryHostCoordinatorOptions { LeaseTimeout = leaseTimeout });
        }

        [Fact]
        public async Task HasLease_WhenLeaseIsAcquired_ReturnsTrue()
        {
            // TODO: Wire these up correctly [BrettSam]
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();

            var host = CreateHost();
            using (host)
            {
                await host.StartAsync();

                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();
                await TestHelpers.Await(() => primaryState.IsPrimary);

                await host.StopAsync();
            }

            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public async Task HasLeaseChanged_WhenLeaseIsAcquiredAndStateChanges_IsFired()
        {
            //TODO: Wire these up correctly [BrettSam]
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var resetEvent = new ManualResetEventSlim();

            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);
            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            // Acquire a lease on the host lock blob
            string leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(15));

            PrimaryHostCoordinator manager = null;

            var host = CreateHost();
            using (host)
            {
                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();

                // We've taken the lease already.
                Assert.False(primaryState.IsPrimary);

                // Now release it, and we should reclaim it.
                await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = leaseId });

                await TestHelpers.Await(() => primaryState.IsPrimary, pollingInterval: 50);

                manager.Dispose();

                Assert.True(resetEvent.IsSet);
                Assert.True(primaryState.IsPrimary, $"{nameof(IPrimaryHostStateProvider.IsPrimary)} was not correctly set to 'true' when lease was acquired.");
            }
            await ClearLeaseBlob(hostId);
        }

        [Fact]
        public async Task HasLeaseChanged_WhenLeaseIsLostAndStateChanges_IsFired()
        {
            //TODO: Wire these up correctly [BrettSam]
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);
            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            var resetEvent = new ManualResetEventSlim();

            PrimaryHostCoordinator manager = null;
            string tempLeaseId = null;

            var host = CreateHost();
            using (host)
            {
                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();
                manager = host.Services.GetServices<IHostedService>().OfType<PrimaryHostCoordinator>().Single();
                var lockManager = host.Services.GetService<IDistributedLockManager>();

                var renewalInterval = TimeSpan.FromSeconds(3);
                try
                {
                    await TestHelpers.Await(() => primaryState.IsPrimary, pollingInterval: 50);

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

                await TestHelpers.Await(() => !primaryState.IsPrimary, pollingInterval: 50);

                Assert.False(primaryState.IsPrimary, $"{nameof(IPrimaryHostStateProvider.IsPrimary)} was not correctly set to 'false' when lease lost.");

                await ClearLeaseBlob(hostId);
            }
        }

        [Fact]
        public async Task Dispose_ReleasesBlobLease()
        {
            //TODO: Wire these up correctly [BrettSam]
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);

            var host = CreateHost();
            using (host)
            {
                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();
                await TestHelpers.Await(() => primaryState.IsPrimary, pollingInterval: 50);

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
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseIsAcquired()
        {
            //TODO: Wire these up correctly [BrettSam]
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();

            var blobMock = new Mock<IDistributedLockManager>();
            blobMock.Setup(b => b.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IDistributedLock>(new FakeLock()));

            var host = CreateHost();
            using (host)
            {
                // Make sure we have enough time to trace the renewal
                await TestHelpers.Await(() => _loggerProvider.GetAllLogMessages().Any(m => m.FormattedMessage.StartsWith("Host lock lease acquired by instance ID ")), 5000, 500);

                LogMessage acquisitionEvent = _loggerProvider.GetAllLogMessages().First();
                Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.FormattedMessage);
                Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, acquisitionEvent.Level);
            }
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseRenewalFails()
        {
            //TODO: Wire these up correctly [BrettSam]
            string hostId = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            var renewResetEvent = new ManualResetEventSlim();

            var blobMock = new Mock<IDistributedLockManager>();
            blobMock.Setup(b => b.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IDistributedLock>(new FakeLock()));

            blobMock.Setup(b => b.RenewAsync(It.IsAny<IDistributedLock>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromException<bool>(new StorageException(new RequestResult { HttpStatusCode = 409 }, "test", null)))
                .Callback(() => renewResetEvent.Set());

            renewResetEvent.Wait(TimeSpan.FromSeconds(10));
            await TestHelpers.Await(() => _loggerProvider.GetAllLogMessages().Count() == 2, 5000, 500);

            LogMessage acquisitionEvent = _loggerProvider.GetAllLogMessages().First();
            Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.FormattedMessage);
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, acquisitionEvent.Level);

            LogMessage renewalEvent = _loggerProvider.GetAllLogMessages().Skip(1).First();
            string pattern = @"Failed to renew host lock lease: Another host has acquired the lease. The last successful renewal completed at (.+) \([0-9]+ milliseconds ago\) with a duration of [0-9]+ milliseconds.";
            Assert.True(Regex.IsMatch(renewalEvent.FormattedMessage, pattern), $"Expected trace event {pattern} not found.");
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, renewalEvent.Level);
        }

        [Fact]
        public async Task DifferentHosts_UsingSameStorageAccount_CanObtainLease()
        {
            //TODO: Wire these up correctly [BrettSam]
            string hostId1 = Guid.NewGuid().ToString();
            string hostId2 = Guid.NewGuid().ToString();
            string instanceId = Guid.NewGuid().ToString();
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);

            var host1 = CreateHost();
            var host2 = CreateHost();
            using (host1)
            using (host2)
            {
                var primaryState1 = host1.Services.GetService<IPrimaryHostStateProvider>();
                var primaryState2 = host2.Services.GetService<IPrimaryHostStateProvider>();

                Task manager1Check = TestHelpers.Await(() => primaryState1.IsPrimary);
                Task manager2Check = TestHelpers.Await(() => primaryState2.IsPrimary);

                await Task.WhenAll(manager1Check, manager2Check);

                await Task.WhenAll(ClearLeaseBlob(hostId1), ClearLeaseBlob(hostId2));
            }
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
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringNames.Storage);
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
