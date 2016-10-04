// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.Blobs.Listeners
{
    public class ScanBlobScanLogHybridPollingStrategyTests
    {
        [Fact]
        public void ScanBlobScanLogHybridPollingStrategyTestBlobListener()
        {
            string containerName = Path.GetRandomFileName();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = account.CreateBlobClient().GetContainerReference(containerName);
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(new TestBlobScanInfoManager());
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            product.Register(container, executor);
            product.Start();

            RunExecuterWithExpectedBlobs(new List<string>(), product, executor);

            string expectedBlobName = CreateAblobAndUploadToContainer(container);

            RunExecuterWithExpectedBlobs(new List<string>() { expectedBlobName }, product, executor);

            // Now run again; shouldn't show up. 
            RunExecuterWithExpectedBlobs(new List<string>(), product, executor);
        }

        [Fact]
        public void TestBlobListenerWithContainerBiggerThanThreshold()
        {
            int testScanBlobLimitPerPoll = 1;
            string containerName = Path.GetRandomFileName();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = account.CreateBlobClient().GetContainerReference(containerName);
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(new TestBlobScanInfoManager());
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            typeof(ScanBlobScanLogHybridPollingStrategy)
                   .GetField("_scanBlobLimitPerPoll", BindingFlags.Instance | BindingFlags.NonPublic)
                   .SetValue(product, testScanBlobLimitPerPoll);

            product.Register(container, executor);
            product.Start();

            // populate with 5 blobs
            List<string> expectedNames = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                expectedNames.Add(CreateAblobAndUploadToContainer(container));
            }

            RunExecuteWithMultiPollingInterval(expectedNames, product, executor, testScanBlobLimitPerPoll);

            // Now run again; shouldn't show up. 
            RunExecuterWithExpectedBlobs(new List<string>(), product, executor);
        }

        [Fact]
        public void TestBlobListenerWithMultipleContainers()
        {
            int testScanBlobLimitPerPoll = 6, containerCount = 2;
            string firstContainerName = Path.GetRandomFileName();
            string secondContainerName = Path.GetRandomFileName();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer firstContainer = account.CreateBlobClient().GetContainerReference(firstContainerName);
            IStorageBlobContainer secondContainer = account.CreateBlobClient().GetContainerReference(secondContainerName);
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(new TestBlobScanInfoManager());
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            typeof(ScanBlobScanLogHybridPollingStrategy)
                   .GetField("_scanBlobLimitPerPoll", BindingFlags.Instance | BindingFlags.NonPublic)
                   .SetValue(product, testScanBlobLimitPerPoll);

            product.Register(firstContainer, executor);
            product.Register(secondContainer, executor);
            product.Start();

            // populate first container with 5 blobs > page size and second with 2 blobs < page size
            // page size is going to be testScanBlobLimitPerPoll / number of container 6/2 = 3
            List<string> firstContainerExpectedNames = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                firstContainerExpectedNames.Add(CreateAblobAndUploadToContainer(firstContainer));
            }

            RunExecuteWithMultiPollingInterval(firstContainerExpectedNames, product, executor, testScanBlobLimitPerPoll / containerCount);

            Thread.Sleep(10);

            List<string> secondContainerExpectedNames = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                secondContainerExpectedNames.Add(CreateAblobAndUploadToContainer(secondContainer));
            }

            RunExecuteWithMultiPollingInterval(secondContainerExpectedNames, product, executor, testScanBlobLimitPerPoll / containerCount);

            // Now run again; shouldn't show up. 
            RunExecuterWithExpectedBlobs(new List<string>(), product, executor);
        }

        [Fact]
        public void BlobPolling_IgnoresClockSkew()
        {
            int testScanBlobLimitPerPoll = 3;
            string containerName = Path.GetRandomFileName();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobClient client = account.CreateBlobClient();
            var now = DateTimeOffset.UtcNow;
            var timeMap = new Dictionary<string, DateTimeOffset>();
            IStorageBlobContainer container = new SkewableFakeStorageBlobContainer(new MemoryBlobStore(), containerName, client,
                (IStorageBlobResultSegment blobs) =>
                {
                    // Simulate some extreme clock skew -- the first one's LastUpdated
                    // wll be 60 seconds ago and the second will be 59 seconds ago.
                    foreach (IStorageBlob blob in blobs.Results)
                    {
                        ((FakeStorageBlobProperties)blob.Properties).LastModified = timeMap[blob.Name];
                    }
                });
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(new TestBlobScanInfoManager());
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            typeof(ScanBlobScanLogHybridPollingStrategy)
                   .GetField("_scanBlobLimitPerPoll", BindingFlags.Instance | BindingFlags.NonPublic)
                   .SetValue(product, testScanBlobLimitPerPoll);

            product.Register(container, executor);
            product.Start();

            List<string> expectedNames = new List<string>();
            expectedNames.Add(CreateAblobAndUploadToContainer(container));
            timeMap[expectedNames.Single()] = now.AddSeconds(-60);
            RunExecuterWithExpectedBlobs(expectedNames, product, executor);

            expectedNames.Clear();

            expectedNames.Add(CreateAblobAndUploadToContainer(container));
            timeMap[expectedNames.Single()] = now.AddSeconds(-59);

            // We should see the new item.
            RunExecuterWithExpectedBlobs(expectedNames, product, executor);
        }

        [Fact]
        public async Task RegisterAsync_InitializesWithScanInfoManager()
        {
            string containerName = Guid.NewGuid().ToString();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = account.CreateBlobClient().GetContainerReference(containerName);
            TestBlobScanInfoManager scanInfoManager = new TestBlobScanInfoManager();
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(scanInfoManager);
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();

            // Create a few blobs.
            for (int i = 0; i < 5; i++)
            {
                CreateAblobAndUploadToContainer(container);
            }

            await scanInfoManager.UpdateLatestScanAsync(account.Credentials.AccountName, containerName, DateTime.UtcNow);
            await product.RegisterAsync(container, executor, CancellationToken.None);

            // delay slightly so we guarantee a later timestamp
            await Task.Delay(10);

            var expectedNames = new List<string>();
            expectedNames.Add(CreateAblobAndUploadToContainer(container));

            RunExecuterWithExpectedBlobs(expectedNames, product, executor);
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesScanInfoManager()
        {
            int testScanBlobLimitPerPoll = 6;
            string firstContainerName = Guid.NewGuid().ToString();
            string secondContainerName = Guid.NewGuid().ToString();
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer firstContainer = account.CreateBlobClient().GetContainerReference(firstContainerName);
            IStorageBlobContainer secondContainer = account.CreateBlobClient().GetContainerReference(secondContainerName);
            TestBlobScanInfoManager testScanInfoManager = new TestBlobScanInfoManager();
            string accountName = account.Credentials.AccountName;
            testScanInfoManager.SetScanInfo(accountName, firstContainerName, DateTime.MinValue);
            testScanInfoManager.SetScanInfo(accountName, secondContainerName, DateTime.MinValue);
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(testScanInfoManager);
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            typeof(ScanBlobScanLogHybridPollingStrategy)
                  .GetField("_scanBlobLimitPerPoll", BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(product, testScanBlobLimitPerPoll);

            await product.RegisterAsync(firstContainer, executor, CancellationToken.None);
            await product.RegisterAsync(secondContainer, executor, CancellationToken.None);

            var firstExpectedNames = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                firstExpectedNames.Add(CreateAblobAndUploadToContainer(firstContainer));
            }
            RunExecuteWithMultiPollingInterval(firstExpectedNames, product, executor, testScanBlobLimitPerPoll / 2);

            // only expect the first container to have updated its scanInfo
            Assert.Equal(1, testScanInfoManager.UpdateCounts[accountName][firstContainerName]);
            int count;
            testScanInfoManager.UpdateCounts[accountName].TryGetValue(secondContainerName, out count);
            Assert.Equal(0, count);

            await Task.Delay(10);

            var secondExpectedNames = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                secondExpectedNames.Add(CreateAblobAndUploadToContainer(secondContainer));
            }
            RunExecuteWithMultiPollingInterval(secondExpectedNames, product, executor, testScanBlobLimitPerPoll / 2);

            // this time, only expect the second container to have updated its scanInfo
            Assert.Equal(1, testScanInfoManager.UpdateCounts[accountName][firstContainerName]);
            Assert.Equal(1, testScanInfoManager.UpdateCounts[accountName][secondContainerName]);
        }

        [Fact]
        public async Task ExecuteAsync_UpdatesScanInfo_WithEarliestFailure()
        {
            int testScanBlobLimitPerPoll = 6;
            string containerName = Guid.NewGuid().ToString();

            // we'll introduce multiple errors to make sure we take the earliest timestamp
            DateTime earliestErrorTime = DateTime.UtcNow;
            var timeMap = new Dictionary<string, DateTimeOffset>();

            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = new SkewableFakeStorageBlobContainer(new MemoryBlobStore(), containerName,
                account.CreateBlobClient(), blobs =>
                {
                    // Set a blob with "throw" to a specific date and time. Make sure the error blob
                    // is earlier than the others.
                    foreach (IStorageBlob blob in blobs.Results)
                    {
                        ((FakeStorageBlobProperties)blob.Properties).LastModified = timeMap[blob.Name];
                    }
                });

            TestBlobScanInfoManager testScanInfoManager = new TestBlobScanInfoManager();
            string accountName = account.Credentials.AccountName;
            testScanInfoManager.SetScanInfo(accountName, containerName, DateTime.MinValue);
            IBlobListenerStrategy product = new ScanBlobScanLogHybridPollingStrategy(testScanInfoManager);
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            typeof(ScanBlobScanLogHybridPollingStrategy)
                  .GetField("_scanBlobLimitPerPoll", BindingFlags.Instance | BindingFlags.NonPublic)
                  .SetValue(product, testScanBlobLimitPerPoll);

            await product.RegisterAsync(container, executor, CancellationToken.None);

            // Induce a failure to make sure the timestamp is earlier than the failure.
            var expectedNames = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                string name;
                if (i % 3 == 0)
                {
                    name = CreateAblobAndUploadToContainer(container, "throw");
                    timeMap[name] = earliestErrorTime.AddMinutes(i);
                }
                else
                {
                    name = CreateAblobAndUploadToContainer(container, "test");
                    timeMap[name] = earliestErrorTime.AddMinutes(10);
                }
                expectedNames.Add(name);
            }
            RunExecuteWithMultiPollingInterval(expectedNames, product, executor, testScanBlobLimitPerPoll);

            DateTime? storedTime = await testScanInfoManager.LoadLatestScanAsync(accountName, containerName);
            Assert.True(storedTime < earliestErrorTime);
            Assert.Equal(1, testScanInfoManager.UpdateCounts[accountName][containerName]);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private void AssertLogPollStrategyUsed(IBlobListenerStrategy product, int containerCount)
        {
            PollLogsStrategy containersRegisteredForLogPolling = (PollLogsStrategy)typeof(ScanBlobScanLogHybridPollingStrategy)
                   .GetField("_pollLogStrategy", BindingFlags.Instance | BindingFlags.NonPublic)
                   .GetValue(product);
            IDictionary<IStorageBlobContainer, ICollection<ITriggerExecutor<IStorageBlob>>> logPollingContainers =
                (IDictionary<IStorageBlobContainer, ICollection<ITriggerExecutor<IStorageBlob>>>)
                typeof(PollLogsStrategy)
                   .GetField("_registrations", BindingFlags.Instance | BindingFlags.NonPublic)
                   .GetValue(containersRegisteredForLogPolling);
            Assert.Equal(logPollingContainers.ToList().Count, containerCount);
        }

        private void RunExecuterWithExpectedBlobsInternal(IDictionary<string, int> blobNameMap, IBlobListenerStrategy product, LambdaBlobTriggerExecutor executor, int expectedCount)
        {
            if (blobNameMap.Count == 0)
            {
                executor.ExecuteLambda = (_) =>
                {
                    throw new InvalidOperationException("shouldn't be any blobs in the container");
                };
                product.Execute().Wait.Wait();
            }
            else
            {
                int count = 0;
                executor.ExecuteLambda = (b) =>
                {
                    Assert.True(blobNameMap.Keys.Any(blob => blob == b.Name));
                    blobNameMap[b.Name]++;

                    if (b.DownloadText() == "throw")
                    {
                        // only increment if it's the first time.
                        // other calls are re-tries.
                        if (blobNameMap[b.Name] == 1)
                        {
                            count++;
                        }
                        return false;
                    }
                    count++;
                    return true;
                };
                product.Execute();
                Assert.Equal(expectedCount, count);
            }
        }

        private void RunExecuterWithExpectedBlobs(List<string> blobNames, IBlobListenerStrategy product, LambdaBlobTriggerExecutor executor)
        {
            var blobNameMap = blobNames.ToDictionary(n => n, n => 0);
            RunExecuterWithExpectedBlobsInternal(blobNameMap, product, executor, blobNames.Count);
        }


        private void RunExecuterWithExpectedBlobs(IDictionary<string, int> blobNameMap, IBlobListenerStrategy product, LambdaBlobTriggerExecutor executor)
        {
            RunExecuterWithExpectedBlobsInternal(blobNameMap, product, executor, blobNameMap.Count);
        }

        private void RunExecuteWithMultiPollingInterval(List<string> expectedBlobNames, IBlobListenerStrategy product, LambdaBlobTriggerExecutor executor, int expectedCount)
        {
            // a map so we can track retries in the event of failures
            Dictionary<string, int> blobNameMap = expectedBlobNames.ToDictionary(n => n, n => 0);

            // make sure it is processed in chunks of "expectedCount" size
            for (int i = 0; i < expectedBlobNames.Count; i += expectedCount)
            {
                RunExecuterWithExpectedBlobsInternal(blobNameMap, product, executor,
                    Math.Min(expectedCount, expectedBlobNames.Count - i));
            }
        }

        private string CreateAblobAndUploadToContainer(IStorageBlobContainer container, string blobContent = "test")
        {
            string blobName = Path.GetRandomFileName().Replace(".", "");
            IStorageBlockBlob blob = container.GetBlockBlobReference(blobName);
            container.CreateIfNotExists();
            blob.UploadText(blobContent);
            return blobName;
        }

        private class LambdaBlobTriggerExecutor : ITriggerExecutor<IStorageBlob>
        {
            public Func<IStorageBlob, bool> ExecuteLambda { get; set; }

            public Task<FunctionResult> ExecuteAsync(IStorageBlob value, CancellationToken cancellationToken)
            {
                bool succeeded = ExecuteLambda.Invoke(value);
                FunctionResult result = new FunctionResult(succeeded);
                return Task.FromResult(result);
            }
        }

        private class SkewableFakeStorageBlobContainer : FakeStorageBlobContainer
        {
            private Action<IStorageBlobResultSegment> _onListBlobsSegmented;

            public SkewableFakeStorageBlobContainer(MemoryBlobStore store, string containerName,
                IStorageBlobClient parent, Action<IStorageBlobResultSegment> OnListBlobsSegmented)
                : base(store, containerName, parent)
            {
                _onListBlobsSegmented = OnListBlobsSegmented;
            }

            public override async Task<IStorageBlobResultSegment> ListBlobsSegmentedAsync(string prefix, bool useFlatBlobListing, BlobListingDetails blobListingDetails, int? maxResults, BlobContinuationToken currentToken, BlobRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
            {
                var results = await base.ListBlobsSegmentedAsync(prefix, useFlatBlobListing, blobListingDetails, maxResults, currentToken, options, operationContext, cancellationToken);
                _onListBlobsSegmented(results);
                return results;
            }
        }

        private class TestBlobScanInfoManager : IBlobScanInfoManager
        {
            private IDictionary<string, IDictionary<string, DateTime>> _latestScans;

            public TestBlobScanInfoManager()
            {
                _latestScans = new Dictionary<string, IDictionary<string, DateTime>>();
                UpdateCounts = new Dictionary<string, IDictionary<string, int>>();
            }

            public IDictionary<string, IDictionary<string, int>> UpdateCounts { get; private set; }

            public Task<DateTime?> LoadLatestScanAsync(string storageAccountName, string containerName)
            {
                DateTime? value = null;
                IDictionary<string, DateTime> accounts;
                if (_latestScans.TryGetValue(storageAccountName, out accounts))
                {
                    DateTime latestScan;
                    if (accounts.TryGetValue(containerName, out latestScan))
                    {
                        value = latestScan;
                    }
                }

                return Task.FromResult(value);
            }

            public Task UpdateLatestScanAsync(string storageAccountName, string containerName, DateTime latestScan)
            {
                SetScanInfo(storageAccountName, containerName, latestScan);
                IncrementCount(storageAccountName, containerName);
                return Task.FromResult(0);
            }

            public void SetScanInfo(string storageAccountName, string containerName, DateTime latestScan)
            {
                IDictionary<string, DateTime> containers;

                if (!_latestScans.TryGetValue(storageAccountName, out containers))
                {
                    _latestScans[storageAccountName] = new Dictionary<string, DateTime>();
                    containers = _latestScans[storageAccountName];
                }

                containers[containerName] = latestScan;
            }

            private void IncrementCount(string storageAccountName, string containerName)
            {
                IDictionary<string, int> counts;
                if (!UpdateCounts.TryGetValue(storageAccountName, out counts))
                {
                    UpdateCounts[storageAccountName] = new Dictionary<string, int>();
                    counts = UpdateCounts[storageAccountName];
                }

                if (counts.ContainsKey(containerName))
                {
                    counts[containerName]++;
                }
                else
                {
                    counts[containerName] = 1;
                }
            }
        }
    }
}
