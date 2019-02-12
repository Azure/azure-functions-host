// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
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
        private TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        private IHost CreateHost(Action<IServiceCollection> configure = null)
        {
            var host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(o =>
                {
                    o.ScriptPath = Path.GetTempPath();
                    o.LogPath = Path.GetTempPath();
                })
                .ConfigureServices(s =>
                {
                    // By default, all hosted services are removed from the test host
                    s.AddSingleton<IHostedService, PrimaryHostCoordinator>();

                    configure?.Invoke(s);
                })
                .ConfigureLogging(b =>
                {
                    b.AddProvider(_loggerProvider);
                })
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
            var host = CreateHost();

            string connectionString = host.GetStorageConnectionString();
            string hostId = host.GetHostId();

            using (host)
            {
                await host.StartAsync();

                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();
                await TestHelpers.Await(() => primaryState.IsPrimary);

                await host.StopAsync();
            }

            await ClearLeaseBlob(connectionString, hostId);
        }

        [Fact]
        public async Task HasLeaseChanged_WhenLeaseIsAcquired()
        {
            var host = CreateHost();

            string connectionString = host.GetStorageConnectionString();
            string hostId = host.GetHostId();

            ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);

            // Acquire a lease on the host lock blob
            string leaseId = await blob.AcquireLeaseAsync(TimeSpan.FromMinutes(1));

            using (host)
            {
                await host.StartAsync();

                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();

                // The test owns the lease, so the host doesn't have it.
                Assert.False(primaryState.IsPrimary);

                // Now release it, and we should reclaim it.
                await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = leaseId });

                await TestHelpers.Await(() => primaryState.IsPrimary,
                    userMessageCallback: () => $"{nameof(IPrimaryHostStateProvider.IsPrimary)} was not correctly set to 'true' when lease was acquired.");

                await host.StopAsync();
            }

            await ClearLeaseBlob(connectionString, hostId);
        }

        [Fact]
        public async Task HasLeaseChanged_WhenLeaseIsLost()
        {
            var host = CreateHost();

            string connectionString = host.GetStorageConnectionString();
            string hostId = host.GetHostId();

            using (host)
            {
                ICloudBlob blob = await GetLockBlobAsync(connectionString, hostId);
                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();
                var manager = host.Services.GetServices<IHostedService>().OfType<PrimaryHostCoordinator>().Single();
                var lockManager = host.Services.GetService<IDistributedLockManager>();
                string tempLeaseId = null;

                await host.StartAsync();

                try
                {
                    await TestHelpers.Await(() => primaryState.IsPrimary);

                    // Release the manager's lease and acquire one with a different id
                    await lockManager.ReleaseLockAsync(manager.LockHandle, CancellationToken.None);
                    tempLeaseId = await blob.AcquireLeaseAsync(TimeSpan.FromSeconds(30), Guid.NewGuid().ToString());

                    await TestHelpers.Await(() => !primaryState.IsPrimary,
                        userMessageCallback: () => $"{nameof(IPrimaryHostStateProvider.IsPrimary)} was not correctly set to 'false' when lease lost.");
                }
                finally
                {
                    if (tempLeaseId != null)
                    {
                        await blob.ReleaseLeaseAsync(new AccessCondition { LeaseId = tempLeaseId });
                    }
                }

                await host.StopAsync();
            }

            await ClearLeaseBlob(connectionString, hostId);
        }

        [Fact]
        public async Task Dispose_ReleasesBlobLease()
        {
            var host = CreateHost();

            string connectionString = host.GetStorageConnectionString();
            string hostId = host.GetHostId();
            var primaryHostCoordinator = host.Services.GetServices<IHostedService>().OfType<PrimaryHostCoordinator>().Single();

            using (host)
            {
                await host.StartAsync();

                var primaryState = host.Services.GetService<IPrimaryHostStateProvider>();
                await TestHelpers.Await(() => primaryState.IsPrimary);

                await host.StopAsync();
            }

            // Container disposal is a fire-and-forget so this service disposal could be delayed. This will force it.
            primaryHostCoordinator.Dispose();

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

            await ClearLeaseBlob(connectionString, hostId);
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseIsAcquired()
        {
            var blobMock = new Mock<IDistributedLockManager>();
            blobMock.Setup(b => b.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IDistributedLock>(new FakeLock()));

            var host = CreateHost(s =>
            {
                s.AddSingleton<IDistributedLockManager>(_ => blobMock.Object);
            });

            string connectionString = host.GetStorageConnectionString();
            string hostId = host.GetHostId();
            string instanceId = host.Services.GetService<ScriptSettingsManager>().AzureWebsiteInstanceId;

            using (host)
            {
                await host.StartAsync();

                // Make sure we have enough time to trace the renewal
                await TestHelpers.Await(() => _loggerProvider.GetAllLogMessages().Any(m => m.FormattedMessage.StartsWith("Host lock lease acquired by instance ID ")), 10000, 500);

                LogMessage acquisitionEvent = _loggerProvider.GetAllLogMessages().First();
                Assert.Contains($"Host lock lease acquired by instance ID '{instanceId}'.", acquisitionEvent.FormattedMessage);
                Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, acquisitionEvent.Level);

                await host.StopAsync();
            }
        }

        [Fact]
        public async Task TraceOutputsMessagesWhenLeaseRenewalFails()
        {
            var renewResetEvent = new ManualResetEventSlim();

            var blobMock = new Mock<IDistributedLockManager>();
            blobMock.Setup(b => b.TryLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IDistributedLock>(new FakeLock()));

            blobMock.Setup(b => b.RenewAsync(It.IsAny<IDistributedLock>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromException<bool>(new StorageException(new RequestResult { HttpStatusCode = 409 }, "test", null)))
                .Callback(() => renewResetEvent.Set());

            var host = CreateHost(s =>
            {
                s.AddSingleton<IDistributedLockManager>(_ => blobMock.Object);
            });

            string hostId = host.GetHostId();
            string instanceId = host.Services.GetService<ScriptSettingsManager>().AzureWebsiteInstanceId;

            using (host)
            {
                await host.StartAsync();

                renewResetEvent.Wait(TimeSpan.FromSeconds(10));
                await TestHelpers.Await(() => _loggerProvider.GetAllLogMessages().Any(m => m.FormattedMessage.StartsWith("Failed to renew host lock lease: ")), 10000, 500);

                await host.StopAsync();
            }

            LogMessage acquisitionEvent = _loggerProvider.GetAllLogMessages().Single(m => m.FormattedMessage.Contains($"Host lock lease acquired by instance ID '{instanceId}'."));
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, acquisitionEvent.Level);

            LogMessage renewalEvent = _loggerProvider.GetAllLogMessages().Single(m => m.FormattedMessage.Contains(@"Failed to renew host lock lease: Another host has acquired the lease."));
            string pattern = @"Failed to renew host lock lease: Another host has acquired the lease. The last successful renewal completed at (.+) \([0-9]+ milliseconds ago\) with a duration of [0-9]+ milliseconds.";
            Assert.True(Regex.IsMatch(renewalEvent.FormattedMessage, pattern), $"Expected trace event {pattern} not found.");
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, renewalEvent.Level);
        }

        [Fact]
        public async Task DifferentHosts_UsingSameStorageAccount_CanObtainLease()
        {
            string hostId1 = Guid.NewGuid().ToString();
            string hostId2 = Guid.NewGuid().ToString();

            var host1 = CreateHost(s =>
            {
                s.AddSingleton<IHostIdProvider>(_ => new FixedHostIdProvider(hostId1));
            });

            var host2 = CreateHost(s =>
            {
                s.AddSingleton<IHostIdProvider>(_ => new FixedHostIdProvider(hostId2));
            });

            string host1ConnectionString = host1.GetStorageConnectionString();
            string host2ConnectionString = host2.GetStorageConnectionString();

            using (host1)
            using (host2)
            {
                await host1.StartAsync();
                await host2.StartAsync();

                var primaryState1 = host1.Services.GetService<IPrimaryHostStateProvider>();
                var primaryState2 = host2.Services.GetService<IPrimaryHostStateProvider>();

                Task manager1Check = TestHelpers.Await(() => primaryState1.IsPrimary);
                Task manager2Check = TestHelpers.Await(() => primaryState2.IsPrimary);

                await Task.WhenAll(manager1Check, manager2Check);

                await host1.StopAsync();
                await host2.StopAsync();
            }

            await Task.WhenAll(ClearLeaseBlob(host1ConnectionString, hostId1), ClearLeaseBlob(host2ConnectionString, hostId2));
        }

        private static async Task<ICloudBlob> GetLockBlobAsync(string accountConnectionString, string hostId)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(accountConnectionString);
            CloudBlobClient client = account.CreateCloudBlobClient();

            var container = client.GetContainerReference(ScriptConstants.AzureWebJobsHostsContainerName);

            await container.CreateIfNotExistsAsync();

            // the StorageDistributedLockManager puts things under the /locks path by default
            CloudBlockBlob blob = container.GetBlockBlobReference("locks/" + PrimaryHostCoordinator.GetBlobName(hostId));
            if (!await blob.ExistsAsync())
            {
                await blob.UploadFromStreamAsync(new MemoryStream());
            }

            return blob;
        }

        private async Task ClearLeaseBlob(string connectionString, string hostId)
        {
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

        private class FixedHostIdProvider : IHostIdProvider
        {
            private readonly string _hostId;

            public FixedHostIdProvider(string hostId)
            {
                _hostId = hostId;
            }

            public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_hostId);
            }
        }
    }
}
