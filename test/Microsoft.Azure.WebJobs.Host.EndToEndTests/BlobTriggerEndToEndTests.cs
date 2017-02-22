// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class BlobTriggerEndToEndTests : IDisposable
    {
        private const string TestArtifactPrefix = "e2etests";
        
        private const string SingleTriggerContainerName = TestArtifactPrefix + "singletrigger-%rnd%";
        private const string PoisonTestContainerName = TestArtifactPrefix + "poison-%rnd%";
        private const string TestBlobName = "test";

        private const string BlobChainContainerName = TestArtifactPrefix + "blobchain-%rnd%";
        private const string BlobChainTriggerBlobName = "blob";
        private const string BlobChainTriggerBlobPath = BlobChainContainerName + "/" + BlobChainTriggerBlobName;
        private const string BlobChainCommittedQueueName = "committed";
        private const string BlobChainIntermediateBlobPath = BlobChainContainerName + "/" + "blob.middle";
        private const string BlobChainOutputBlobName = "blob.out";
        private const string BlobChainOutputBlobPath = BlobChainContainerName + "/" + BlobChainOutputBlobName;

        private static ManualResetEvent _completedEvent;
        private static int _timesProcessed;

        private readonly CloudBlobContainer _testContainer;
        private readonly JobHostConfiguration _hostConfiguration;
        private readonly CloudStorageAccount _storageAccount;
        private readonly RandomNameResolver _nameResolver;

        private static object _syncLock = new object();
        private static List<string> poisonBlobMessages = new List<string>();

        public BlobTriggerEndToEndTests()
        {
            _timesProcessed = 0;

            _nameResolver = new RandomNameResolver();
            _hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = _nameResolver,
                TypeLocator = new FakeTypeLocator(this.GetType()),
            };

            _storageAccount = CloudStorageAccount.Parse(_hostConfiguration.StorageConnectionString);
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            _testContainer = blobClient.GetContainerReference(_nameResolver.ResolveInString(SingleTriggerContainerName));
            Assert.False(_testContainer.Exists());
            _testContainer.Create();
        }

        public static void BlobProcessorPrimary(
            [BlobTrigger(PoisonTestContainerName + "/{name}")] string input)
        {
            // throw to generate a poison blob message
            throw new Exception();
        }

        // process the poison queue for the primary storage account
        public static void PoisonBlobQueueProcessorPrimary(
            [QueueTrigger("webjobs-blobtrigger-poison")] JObject message)
        {
            lock (_syncLock)
            {
                string blobName = (string)message["BlobName"];
                poisonBlobMessages.Add(blobName);
            }
        }

        public static void BlobProcessorSecondary(
            [StorageAccount("SecondaryStorage")]
            [BlobTrigger(PoisonTestContainerName + "/{name}")] string input)
        {
            // throw to generate a poison blob message
            throw new Exception();
        }

        // process the poison queue for the secondary storage account
        public static void PoisonBlobQueueProcessorSecondary(
            [StorageAccount("SecondaryStorage")]
            [QueueTrigger("webjobs-blobtrigger-poison")] JObject message)
        {
            lock (_syncLock)
            {
                string blobName = (string)message["BlobName"];
                poisonBlobMessages.Add(blobName);
            }
        }

        public static void SingleBlobTrigger(
            [BlobTrigger(SingleTriggerContainerName + "/{name}")] string sleepTimeInSeconds)
        {
            Interlocked.Increment(ref _timesProcessed);

            int sleepTime = int.Parse(sleepTimeInSeconds) * 1000;
            Thread.Sleep(sleepTime);

            _completedEvent.Set();
        }

        public static void BlobChainStepOne(
            [BlobTrigger(BlobChainTriggerBlobPath)] TextReader input,
            [Blob(BlobChainIntermediateBlobPath)] TextWriter output)
        {
            string content = input.ReadToEnd();
            output.Write(content);
        }

        public static void BlobChainStepTwo(
            [BlobTrigger(BlobChainIntermediateBlobPath)] TextReader input,
            [Blob(BlobChainOutputBlobPath)] TextWriter output, 
            [Queue(BlobChainCommittedQueueName)] out string committed)
        {
            string content = input.ReadToEnd();
            output.Write("*" + content + "*");
            committed = String.Empty;
        }

        public static void BlobChainStepThree([QueueTrigger(BlobChainCommittedQueueName)] string ignore)
        {
            _completedEvent.Set();
        }

        [Theory]
        [InlineData("AzureWebJobsSecondaryStorage")]
        [InlineData("AzureWebJobsStorage")]
        public async Task PoisonMessage_CreatedInCorrectStorageAccount(string storageAccountSetting)
        {
            poisonBlobMessages.Clear();

            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable(storageAccountSetting));
            var blobClient = storageAccount.CreateCloudBlobClient();
            var containerName = _nameResolver.ResolveInString(PoisonTestContainerName);
            var container = blobClient.GetContainerReference(containerName);
            container.Create();

            var blobName = Guid.NewGuid().ToString();
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            blob.UploadText("0");

            using (JobHost host = new JobHost(_hostConfiguration))
            {
                host.Start();

                // wait for the poison message to be handled
                await TestHelpers.Await(() =>
                {
                    return poisonBlobMessages.Contains(blobName);
                });
            }
        }

        [Fact]
        public void BlobGetsProcessedOnlyOnce_SingleHost()
        {
            TextWriter hold = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            CloudBlockBlob blob = _testContainer.GetBlockBlobReference(TestBlobName);
            blob.UploadText("0");

            int timeToProcess;

            // Process the blob first
            using (_completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                DateTime startTime = DateTime.Now;

                host.Start();
                Assert.True(_completedEvent.WaitOne(TimeSpan.FromSeconds(60)));

                timeToProcess = (int)(DateTime.Now - startTime).TotalMilliseconds;

                Console.SetOut(hold);

                Assert.Equal(1, _timesProcessed);

                string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                var executions = consoleOutputLines.Where(p => p.Contains("Executing"));
                Assert.Equal(1, executions.Count());
                Assert.StartsWith(string.Format("Executing 'BlobTriggerEndToEndTests.SingleBlobTrigger' (Reason='New blob detected: {0}/{1}', Id=", blob.Container.Name, blob.Name), executions.Single());
            }

            // Then start again and make sure the blob doesn't get reprocessed
            // wait twice the amount of time required to process first before 
            // deciding that it doesn't get reprocessed
            using (_completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                host.Start();

                bool blobReprocessed = _completedEvent.WaitOne(2 * timeToProcess);

                Assert.False(blobReprocessed);
            }

            Assert.Equal(1, _timesProcessed);
        }

        [Fact]
        public void BlobChainTest()
        {
            // write the initial trigger blob to start the chain
            var blobClient = _storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(_nameResolver.ResolveInString(BlobChainContainerName));
            container.CreateIfNotExists();
            CloudBlockBlob blob = container.GetBlockBlobReference(BlobChainTriggerBlobName);
            blob.UploadText("0");

            using (_completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(_hostConfiguration))
            {
                host.Start();
                Assert.True(_completedEvent.WaitOne(TimeSpan.FromSeconds(60)));
            }
        }

        [Fact]
        public void BlobGetsProcessedOnlyOnce_MultipleHosts()
        {
            _testContainer
                .GetBlockBlobReference(TestBlobName)
                .UploadText("10");

            using (_completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host1 = new JobHost(_hostConfiguration))
            using (JobHost host2 = new JobHost(_hostConfiguration))
            {
                host1.Start();
                host2.Start();

                Assert.True(_completedEvent.WaitOne(TimeSpan.FromSeconds(60)));
            }

            Assert.Equal(1, _timesProcessed);
        }

        public void Dispose()
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            foreach (var testContainer in blobClient.ListContainers(TestArtifactPrefix))
            {
                testContainer.Delete();
            }
        }
    }
}
