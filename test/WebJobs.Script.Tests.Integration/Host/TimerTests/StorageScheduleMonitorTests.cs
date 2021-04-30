// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host.TimerTests
{
    // Borrowed From WebJobs Extensions 
    [Trait("Category", "E2E")]
    public class StorageScheduleMonitorTests
    {
        private const string TestTimerName = "TestProgram.TestTimer";
        private const string TestHostId = "testhostid";
        private readonly AzureStorageScheduleMonitor _scheduleMonitor;

        public StorageScheduleMonitorTests()
        {
            _scheduleMonitor = CreateScheduleMonitor(TestHostId);

            Cleanup().GetAwaiter().GetResult();
        }

        [Fact]
        public void TimerStatusPath_ReturnsExpectedDirectory()
        {
            string path = _scheduleMonitor.TimerStatusPath;
            string expectedPath = string.Format("timers/{0}", TestHostId);
            Assert.Equal(expectedPath, path);
        }

        [Fact]
        public void TimerStatusDirectory_HostIdNull_Throws()
        {
            AzureStorageScheduleMonitor localScheduleMonitor = (AzureStorageScheduleMonitor)CreateScheduleMonitor(null);

            string path = null;
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                path = localScheduleMonitor.TimerStatusPath;
            });

            Assert.Equal("Unable to determine host ID.", ex.Message);
        }

        [Fact]
        public async Task GetStatusAsync_ReturnsExpectedStatus()
        {
            // no status, so should return null
            ScheduleStatus status = await _scheduleMonitor.GetStatusAsync(TestTimerName);
            Assert.Null(status);

            // update the status
            ScheduleStatus expected = new ScheduleStatus
            {
                Last = DateTime.Now.AddMinutes(-5),
                Next = DateTime.Now.AddMinutes(5),
                LastUpdated = DateTime.Now.AddMinutes(-5),
            };
            await _scheduleMonitor.UpdateStatusAsync(TestTimerName, expected);

            // expect the status to be returned
            status = await _scheduleMonitor.GetStatusAsync(TestTimerName);
            Assert.Equal(expected.Last, status.Last);
            Assert.Equal(expected.Next, status.Next);
            Assert.Equal(expected.LastUpdated, status.LastUpdated);
        }

        [Fact]
        public async Task UpdateStatusAsync_MultipleUpdates()
        {
            // no status, so should return null
            ScheduleStatus status = await _scheduleMonitor.GetStatusAsync(TestTimerName);
            Assert.Null(status);

            // update the status
            ScheduleStatus expected = new ScheduleStatus
            {
                Last = DateTime.Now.AddMinutes(-5),
                Next = DateTime.Now.AddMinutes(5),
                LastUpdated = DateTime.Now.AddMinutes(-5),
            };
            await _scheduleMonitor.UpdateStatusAsync(TestTimerName, expected);

            // expect the status to be returned
            status = await _scheduleMonitor.GetStatusAsync(TestTimerName);
            Assert.Equal(expected.Last, status.Last);
            Assert.Equal(expected.Next, status.Next);
            Assert.Equal(expected.LastUpdated, status.LastUpdated);

            // update the status again
            ScheduleStatus expected2 = new ScheduleStatus
            {
                Last = DateTime.Now.AddMinutes(-10),
                Next = DateTime.Now.AddMinutes(10),
                LastUpdated = DateTime.Now.AddMinutes(-10),
            };
            await _scheduleMonitor.UpdateStatusAsync(TestTimerName, expected2);

            // expect the status to be returned
            status = await _scheduleMonitor.GetStatusAsync(TestTimerName);
            Assert.Equal(expected2.Last, status.Last);
            Assert.Equal(expected2.Next, status.Next);
            Assert.Equal(expected2.LastUpdated, status.LastUpdated);
        }

        [Fact]
        public async Task UpdateStatusAsync_MultipleFunctions()
        {
            // update status for 3 functions
            ScheduleStatus expected = new ScheduleStatus
            {
                Last = DateTime.Now.Subtract(TimeSpan.FromMinutes(5)),
                Next = DateTime.Now.AddMinutes(5)
            };
            for (int i = 0; i < 3; i++)
            {
                await _scheduleMonitor.UpdateStatusAsync(TestTimerName + i.ToString(), expected);
            }

            var blobList = new List<BlobHierarchyItem>();
            var segmentResult = _scheduleMonitor.ContainerClient.GetBlobsByHierarchyAsync(prefix: _scheduleMonitor.TimerStatusPath);
            var asyncEnumerator = segmentResult.GetAsyncEnumerator();

            while (await asyncEnumerator.MoveNextAsync())
            {
                blobList.Add(asyncEnumerator.Current);
            }

            var statuses = blobList.Select(b => b.Blob.Name).ToArray();
            Assert.Equal(3, statuses.Length);
            Assert.Equal("timers/testhostid/TestProgram.TestTimer0/status", statuses[0]);
            Assert.Equal("timers/testhostid/TestProgram.TestTimer1/status", statuses[1]);
            Assert.Equal("timers/testhostid/TestProgram.TestTimer2/status", statuses[2]);
        }

        private async Task Cleanup()
        {
            var segmentResult = _scheduleMonitor.ContainerClient.GetBlobsByHierarchyAsync(prefix: _scheduleMonitor.TimerStatusPath);
            var asyncEnumerator = segmentResult.GetAsyncEnumerator();

            while (await asyncEnumerator.MoveNextAsync())
            {
                _scheduleMonitor.ContainerClient.DeleteBlobIfExistsAsync(asyncEnumerator.Current.Blob.Name).Wait();
            }
        }

        public void Dispose()
        {
            Cleanup().GetAwaiter().GetResult();
        }

        AzureStorageScheduleMonitor CreateScheduleMonitor(string hostId = null)
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            var lockContainerManager = new DistributedLockManagerContainerProvider();
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new TestLoggerProvider());

            var azureStorageProvider = TestHelpers.GetAzureStorageProvider(config);
            return new AzureStorageScheduleMonitor(new TestIdProvider(hostId), loggerFactory, azureStorageProvider);
        }

        private class TestIdProvider : IHostIdProvider
        {
            private readonly string _hostId;

            public TestIdProvider(string hostId)
            {
                _hostId = hostId;
            }

            public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<string>(_hostId);
            }
        }
    }
}
