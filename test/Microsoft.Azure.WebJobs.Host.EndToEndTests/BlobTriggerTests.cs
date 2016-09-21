// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class BlobTriggerTests : IDisposable
    {
        private const string TestArtifactPrefix = "e2etestsingletrigger";
        private const string ContainerName = TestArtifactPrefix + "-%rnd%";
        private const string BlobName = "test";

        private static ManualResetEvent _blobProcessedEvent;
        private static int _timesProcessed;

        private readonly CloudBlobContainer _testContainer;
        private readonly JobHostConfiguration _hostConfiguration;
        private readonly CloudStorageAccount _storageAccount;
        private readonly RandomNameResolver _nameResolver;

        public BlobTriggerTests()
        {
            _timesProcessed = 0;

            _nameResolver = new RandomNameResolver();
            _hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = _nameResolver,
                TypeLocator = new FakeTypeLocator(typeof(BlobTriggerTests)),
            };

            _storageAccount = CloudStorageAccount.Parse(_hostConfiguration.StorageConnectionString);
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            _testContainer = blobClient.GetContainerReference(_nameResolver.ResolveInString(ContainerName));
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

        public static void PoisonBlobTrigger([BlobTrigger(ContainerName + "/{name}"), StorageAccount("SecondaryStorage")] string input)
        {
            throw new Exception();
        }

        public void Dispose()
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            foreach (var testContainer in blobClient.ListContainers(TestArtifactPrefix))
            {
                testContainer.Delete();
            }
        }

        [Fact]
        public void PoisonQueue_CreatedOnTriggerStorage()
        {
            TextWriter hold = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            CloudStorageAccount secondary = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsSecondaryStorage"));
            var client = secondary.CreateCloudBlobClient();
            var container = client.GetContainerReference(_nameResolver.ResolveInString(ContainerName));
            container.Create();
            CloudBlockBlob blob = container.GetBlockBlobReference(BlobName);
            blob.UploadText("0");

            // Process the blob first
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                DateTime startTime = DateTime.Now;

                host.Start();

                Task.Delay(20000).GetAwaiter().GetResult();

                host.Stop();
            }
        }

        [Fact]
        public void BlobGetsProcessedOnlyOnce_SingleHost()
        {
            TextWriter hold = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            CloudBlockBlob blob = _testContainer.GetBlockBlobReference(BlobName);
            blob.UploadText("0");

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

                Console.SetOut(hold);

                Assert.Equal(1, _timesProcessed);

                string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                var executions = consoleOutputLines.Where(p => p.Contains("Executing"));
                Assert.Equal(1, executions.Count());
                Assert.StartsWith(string.Format("Executing 'BlobTriggerTests.SingleBlobTrigger' (Reason='New blob detected: {0}/{1}', Id=", blob.Container.Name, blob.Name), executions.Single());
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

            Assert.Equal(1, _timesProcessed);
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
