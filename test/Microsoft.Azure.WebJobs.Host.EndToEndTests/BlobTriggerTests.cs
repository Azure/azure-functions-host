// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class BlobTriggerTests : IDisposable
    {
        private const string ContainerName = "singletrigger-%rnd%";
        private const string BlobName = "test";

        private static ManualResetEvent _blobProcessedEvent;
        private static int _timesProcessed;

        private readonly CloudBlobContainer _testContainer;
        private readonly JobHostConfiguration _hostConfiguration;

        public BlobTriggerTests()
        {
            _timesProcessed = 0;

            RandomNameResolver nameResolver = new RandomNameResolver();
            _hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = nameResolver,
                TypeLocator = new FakeTypeLocator(typeof(BlobTriggerTests)),
            };

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_hostConfiguration.StorageConnectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            _testContainer = blobClient.GetContainerReference(nameResolver.ResolveInString(ContainerName));
            Assert.False(_testContainer.Exists());
            _testContainer.Create();
        }

        public static void SingleBlobTrigger([BlobTrigger(ContainerName + "/{name}")] string sleepTimeInSeconds)
        {
            Interlocked.Increment(ref _timesProcessed);

            int sleepTime = int.Parse(sleepTimeInSeconds) * 1000;
            Thread.Sleep(sleepTime);

            _blobProcessedEvent.Set();
        }

        public void Dispose()
        {
            _testContainer.Delete();
        }

        [Fact]
        public void BlobGetsProcessedOnlyOnce_SingleHost()
        {
            _testContainer
                .GetBlockBlobReference(BlobName)
                .UploadText("0");

            int timeToProcess;

            // Process the blob first
            using (_blobProcessedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                DateTime startTime = DateTime.Now;

                host.Start();
                Assert.True(_blobProcessedEvent.WaitOne(TimeSpan.FromSeconds(60)));

                timeToProcess = (int)(DateTime.Now - startTime).TotalMilliseconds;

                host.Stop();
            }

            // Then start again and make sure the blob doesn't get reprocessed
            // wait twice the amount of time required to process first before 
            // deciding that it doesn't get reprocessed
            using (_blobProcessedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                host.Start();

                bool blobReprocessed = _blobProcessedEvent.WaitOne(2 * timeToProcess);

                host.Stop();

                Assert.False(blobReprocessed);
            }
        }

        [Fact]
        public void BlobGetsProcessedOnlyOnce_MultipleHosts()
        {
            _testContainer
                .GetBlockBlobReference(BlobName)
                .UploadText("10");

            using (_blobProcessedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host1 = new JobHost(_hostConfiguration))
            using (JobHost host2 = new JobHost(_hostConfiguration))
            {
                host1.Start();
                host2.Start();

                Assert.True(_blobProcessedEvent.WaitOne(TimeSpan.FromSeconds(60)));

                host1.Stop();
                host2.Stop();
            }

            Assert.Equal(1, _timesProcessed);
        }
    }
}
