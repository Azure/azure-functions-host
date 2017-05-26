// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
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

        private readonly CloudBlobContainer _testContainer;
        private readonly CloudStorageAccount _storageAccount;
        private readonly RandomNameResolver _nameResolver;

        private static object _syncLock = new object();

        public BlobTriggerEndToEndTests()
        {
            _nameResolver = new RandomNameResolver();

            var storageConnectionString = new JobHostConfiguration().StorageConnectionString;
            _storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            _testContainer = blobClient.GetContainerReference(_nameResolver.ResolveInString(SingleTriggerContainerName));
            Assert.False(_testContainer.ExistsAsync().Result);
            _testContainer.CreateAsync().Wait();
        }

        public JobHostConfiguration NewConfig<TProgram>(TProgram program, params object[] services)
        {
            JobHostConfiguration config = new JobHostConfiguration();

            var activator = new FakeActivator();
            activator.Add(program);
            config.TypeLocator = new FakeTypeLocator(typeof(TProgram));
            config.JobActivator = activator;
            
            config.AddServices(services);
            config.AddServices(_nameResolver);
            config.AddService<IWebJobsExceptionHandler>(new TestExceptionHandler());
            return config;
        }

        public class Poison_Program
        {
            public List<string> _poisonBlobMessages = new List<string>();

            public void BlobProcessorPrimary(
                [BlobTrigger(PoisonTestContainerName + "/{name}")] string input)
            {
                // throw to generate a poison blob message
                throw new Exception();
            }

            // process the poison queue for the primary storage account
            public void PoisonBlobQueueProcessorPrimary(
                [QueueTrigger("webjobs-blobtrigger-poison")] JObject message)
            {
                lock (_syncLock)
                {
                    string blobName = (string)message["BlobName"];
                    _poisonBlobMessages.Add(blobName);
                }
            }

            public void BlobProcessorSecondary(
                [StorageAccount("SecondaryStorage")]
            [BlobTrigger(PoisonTestContainerName + "/{name}")] string input)
            {
                // throw to generate a poison blob message
                throw new Exception();
            }

            // process the poison queue for the secondary storage account
            public void PoisonBlobQueueProcessorSecondary(
                [StorageAccount("SecondaryStorage")]
            [QueueTrigger("webjobs-blobtrigger-poison")] JObject message)
            {
                lock (_syncLock)
                {
                    string blobName = (string)message["BlobName"];
                    _poisonBlobMessages.Add(blobName);
                }
            }
        }

        public class BlobGetsProcessedOnlyOnce_SingleHost_Program
        {
            public int _timesProcessed;
            public ManualResetEvent _completedEvent;

            public void SingleBlobTrigger(
                [BlobTrigger(SingleTriggerContainerName + "/{name}")] string sleepTimeInSeconds)
            {
                Interlocked.Increment(ref _timesProcessed);

                int sleepTime = int.Parse(sleepTimeInSeconds) * 1000;
                Thread.Sleep(sleepTime);

                _completedEvent.Set();
            }
        }

        public class BlobChainTest_Program
        {
            public ManualResetEvent _completedEvent;

            public void BlobChainStepOne(
                [BlobTrigger(BlobChainTriggerBlobPath)] TextReader input,
                [Blob(BlobChainIntermediateBlobPath)] TextWriter output)
            {
                string content = input.ReadToEnd();
                output.Write(content);
            }

            public void BlobChainStepTwo(
                [BlobTrigger(BlobChainIntermediateBlobPath)] TextReader input,
                [Blob(BlobChainOutputBlobPath)] TextWriter output,
                [Queue(BlobChainCommittedQueueName)] out string committed)
            {
                string content = input.ReadToEnd();
                output.Write("*" + content + "*");
                committed = String.Empty;
            }

            public void BlobChainStepThree([QueueTrigger(BlobChainCommittedQueueName)] string ignore)
            {
                _completedEvent.Set();
            }
        }

        [Theory]
        [InlineData("AzureWebJobsSecondaryStorage")]
        [InlineData("AzureWebJobsStorage")]
        public async Task PoisonMessage_CreatedInCorrectStorageAccount(string storageAccountSetting)
        {
            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable(storageAccountSetting));
            var blobClient = storageAccount.CreateCloudBlobClient();
            var containerName = _nameResolver.ResolveInString(PoisonTestContainerName);
            var container = blobClient.GetContainerReference(containerName);
            await container.CreateAsync();

            var blobName = Guid.NewGuid().ToString();
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            await blob.UploadTextAsync("0");

            var prog = new Poison_Program();
            var config = NewConfig(prog);

            using (JobHost host = new JobHost(config))
            {
                host.Start();

                // wait for the poison message to be handled
                await TestHelpers.Await(() =>
                {
                    return prog._poisonBlobMessages.Contains(blobName);
                });
            }
        }

        [Fact]
        public async Task BlobGetsProcessedOnlyOnce_SingleHost()
        {
            TextWriter hold = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            CloudBlockBlob blob = _testContainer.GetBlockBlobReference(TestBlobName);
            await blob.UploadTextAsync("0");

            int timeToProcess;

            var prog = new BlobGetsProcessedOnlyOnce_SingleHost_Program();
            var config = NewConfig(prog);
                        
            // Process the blob first
            using (prog._completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(config))
            {
                DateTime startTime = DateTime.Now;

                host.Start();
                Assert.True(prog._completedEvent.WaitOne(TimeSpan.FromSeconds(60)));

                timeToProcess = (int)(DateTime.Now - startTime).TotalMilliseconds;

                Console.SetOut(hold);

                Assert.Equal(1, prog._timesProcessed);

                string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                var executions = consoleOutputLines.Where(p => p.Contains("Executing"));
                Assert.Equal(1, executions.Count());
                Assert.StartsWith(string.Format("Executing 'BlobGetsProcessedOnlyOnce_SingleHost_Program.SingleBlobTrigger' (Reason='New blob detected: {0}/{1}', Id=", blob.Container.Name, blob.Name), executions.Single());
            }

            // Then start again and make sure the blob doesn't get reprocessed
            // wait twice the amount of time required to process first before 
            // deciding that it doesn't get reprocessed
            using (prog._completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(config))
            {
                host.Start();

                bool blobReprocessed = prog._completedEvent.WaitOne(2 * timeToProcess);

                Assert.False(blobReprocessed);
            }

            Assert.Equal(1, prog._timesProcessed);
        }

        [Fact]
        public async Task BlobChainTest()
        {
            // write the initial trigger blob to start the chain
            var blobClient = _storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(_nameResolver.ResolveInString(BlobChainContainerName));
            await container.CreateIfNotExistsAsync();
            CloudBlockBlob blob = container.GetBlockBlobReference(BlobChainTriggerBlobName);
            await blob.UploadTextAsync("0");

            var prog = new BlobChainTest_Program();
            var config = NewConfig(prog);

            using (prog._completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host = new JobHost(config))
            {
                host.Start();
                Assert.True(prog._completedEvent.WaitOne(TimeSpan.FromSeconds(60)));
            }
        }

        [Fact]
        public async Task BlobGetsProcessedOnlyOnce_MultipleHosts()
        {
            await _testContainer
                .GetBlockBlobReference(TestBlobName)
                .UploadTextAsync("10");


            var prog = new BlobGetsProcessedOnlyOnce_SingleHost_Program();
            var config = NewConfig(prog);


            using (prog._completedEvent = new ManualResetEvent(initialState: false))
            using (JobHost host1 = new JobHost(config))
            using (JobHost host2 = new JobHost(config))
            {
                host1.Start();
                host2.Start();

                Assert.True(prog._completedEvent.WaitOne(TimeSpan.FromSeconds(60)));
            }

            Assert.Equal(1, prog._timesProcessed);
        }

        public void Dispose()
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            foreach (var testContainer in blobClient.ListContainersSegmentedAsync(TestArtifactPrefix, null).Result.Results)
            {
                testContainer.DeleteAsync();
            }
        }
    }
}
