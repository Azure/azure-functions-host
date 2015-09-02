// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class AsyncChainEndToEndTests : IClassFixture<AsyncChainEndToEndTests.TestFixture>
    {
        private const string TestArtifactsPrefix = "asynce2e";

        private const string ContainerName = TestArtifactsPrefix + "%rnd%";

        private const string NonWebJobsBlobName = "NonWebJobs";
        private const string Blob1Name = "Blob1";
        private const string Blob2Name = "Blob2";

        private const string Queue1Name = TestArtifactsPrefix + "q1%rnd%";
        private const string Queue2Name = TestArtifactsPrefix + "q2%rnd%";

        private static CloudStorageAccount _storageAccount;

        private static RandomNameResolver _resolver;
        private static JobHostConfiguration _hostConfig;

        private static EventWaitHandle _functionCompletedEvent;

        private static string _finalBlobContent;

        public AsyncChainEndToEndTests(TestFixture fixture)
        {
            _resolver = new RandomNameResolver();
            _hostConfig = new JobHostConfiguration()
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(typeof(AsyncChainEndToEndTests))
            };

            _hostConfig.Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);

            _storageAccount = fixture.StorageAccount;
        }

        [Fact]
        public async Task AsyncChainEndToEnd()
        {
            using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
            {
                TextWriter hold = Console.Out;
                StringWriter consoleOutput = new StringWriter();
                Console.SetOut(consoleOutput);

                await AsyncChainEndToEndInternal();

                string firstQueueName = _resolver.ResolveInString(Queue1Name);
                string secondQueueName = _resolver.ResolveInString(Queue2Name);
                string blobContainerName = _resolver.ResolveInString(ContainerName);
                string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None).OrderBy(p => p).ToArray();
                string[] expectedOutputLines = new string[]
                {
                    "Found the following functions:",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.WriteStartDataMessageToQueue",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueToQueueAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueToBlobAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.BlobToBlobAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.ReadResultBlob",
                    "Job host started",
                    "Executing: 'AsyncChainEndToEndTests.WriteStartDataMessageToQueue' - Reason: 'This function was programmatically called via the host APIs.'",
                    "Executed: 'AsyncChainEndToEndTests.WriteStartDataMessageToQueue' (Succeeded)",
                    string.Format("Executing: 'AsyncChainEndToEndTests.QueueToQueueAsync' - Reason: 'New queue message detected on '{0}'.'", firstQueueName),
                    "Executed: 'AsyncChainEndToEndTests.QueueToQueueAsync' (Succeeded)",
                    string.Format("Executing: 'AsyncChainEndToEndTests.QueueToBlobAsync' - Reason: 'New queue message detected on '{0}'.'", secondQueueName),
                    "Executed: 'AsyncChainEndToEndTests.QueueToBlobAsync' (Succeeded)",
                    string.Format("Executing: 'AsyncChainEndToEndTests.BlobToBlobAsync' - Reason: 'New blob detected: {0}/Blob1'", blobContainerName),
                    "Executed: 'AsyncChainEndToEndTests.BlobToBlobAsync' (Succeeded)",
                    "Job host stopped",
                    "Executing: 'AsyncChainEndToEndTests.ReadResultBlob' - Reason: 'This function was programmatically called via the host APIs.'",
                    "Executed: 'AsyncChainEndToEndTests.ReadResultBlob' (Succeeded)"
                }.OrderBy(p => p).ToArray();

                bool hasError = consoleOutputLines.Any(p => p.Contains("Function had errors"));
                if (!hasError)
                {
                    Assert.Equal(
                    string.Join(Environment.NewLine, expectedOutputLines),
                    string.Join(Environment.NewLine, consoleOutputLines)
                    );
                }

                Console.SetOut(hold);
            }
        }

        [Fact]
        public async Task AsyncChainEndToEnd_CustomFactories()
        {
            using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
            {
                CustomQueueProcessorFactory queueProcessorFactory = new CustomQueueProcessorFactory();
                _hostConfig.Queues.QueueProcessorFactory = queueProcessorFactory;

                CustomStorageClientFactory storageClientFactory = new CustomStorageClientFactory();
                _hostConfig.StorageClientFactory = storageClientFactory;

                await AsyncChainEndToEndInternal();

                Assert.Equal(2, queueProcessorFactory.CustomQueueProcessors.Count);
                Assert.True(queueProcessorFactory.CustomQueueProcessors.All(p => p.Context.Queue.Name.StartsWith("asynce2eq")));
                Assert.True(queueProcessorFactory.CustomQueueProcessors.Sum(p => p.BeginProcessingCount) >= 2);
                Assert.True(queueProcessorFactory.CustomQueueProcessors.Sum(p => p.CompleteProcessingCount) >= 2);

                Assert.Equal(13, storageClientFactory.TotalBlobClientCount);
                Assert.Equal(6, storageClientFactory.TotalQueueClientCount);
                Assert.Equal(0, storageClientFactory.TotalTableClientCount);

                Assert.Equal(5, storageClientFactory.ParameterBlobClientCount);
                Assert.Equal(4, storageClientFactory.ParameterQueueClientCount);
                Assert.Equal(0, storageClientFactory.ParameterTableClientCount);
            }
        }

        [Fact]
        public async Task TraceWriterLogging()
        {
            TextWriter hold = Console.Out;
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);
            
            using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
            {
                TestTraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
                _hostConfig.Tracing.Trace = trace;
                JobHost host = new JobHost(_hostConfig);

                await host.StartAsync();
                await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("WriteStartDataMessageToQueue"));

                _functionCompletedEvent.WaitOne();

                // ensure all logs have had a chance to flush
                await Task.Delay(3000);

                await host.StopAsync();

                bool hasError = string.Join(Environment.NewLine, trace.Traces.Where(p => p.Contains("Error"))).Any();
                if (!hasError)
                {
                    Assert.Equal(14, trace.Traces.Count);
                    Assert.NotNull(trace.Traces.SingleOrDefault(p => p.Contains("User TraceWriter log")));
                    Assert.NotNull(trace.Traces.SingleOrDefault(p => p.Contains("User TextWriter log (TestParam)")));
                    Assert.NotNull(trace.Traces.SingleOrDefault(p => p.Contains("Another User TextWriter log")));

                    string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                    Assert.Equal(16, consoleOutputLines.Length);
                    Assert.Null(consoleOutputLines.SingleOrDefault(p => p.Contains("User TraceWriter log")));
                    Assert.Null(consoleOutputLines.SingleOrDefault(p => p.Contains("User TextWriter log (TestParam)")));
                }
            }

            Console.SetOut(hold);
        }

        [NoAutomaticTrigger]
        public static async Task WriteStartDataMessageToQueue(
            [Queue(Queue1Name)] ICollector<string> queueMessages,
            [Blob(ContainerName + "/" + NonWebJobsBlobName, FileAccess.Write)] Stream nonSdkBlob,
            CancellationToken token)
        {
            queueMessages.Add(" works");

            byte[] messageBytes = Encoding.UTF8.GetBytes("async");
            await nonSdkBlob.WriteAsync(messageBytes, 0, messageBytes.Length);
        }

        public static async Task QueueToQueueAsync(
            [QueueTrigger(Queue1Name)] string message,
            [Queue(Queue2Name)] IAsyncCollector<string> output,
            CancellationToken token,
            TraceWriter trace)
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(_resolver.ResolveInString(ContainerName));
            CloudBlockBlob blob = container.GetBlockBlobReference(NonWebJobsBlobName);
            string blobContent = await blob.DownloadTextAsync();

            trace.Info("User TraceWriter log");

            await output.AddAsync(blobContent + message);
        }

        public static async Task QueueToBlobAsync(
            [QueueTrigger(Queue2Name)] string message,
            [Blob(ContainerName + "/" + Blob1Name, FileAccess.Write)] Stream blobStream,
            CancellationToken token,
            TextWriter log)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            log.WriteLine("User TextWriter log ({0})", "TestParam");
            log.Write("Another User TextWriter log");

            await blobStream.WriteAsync(messageBytes, 0, messageBytes.Length);
        }

        public static async Task BlobToBlobAsync(
            [BlobTrigger(ContainerName + "/" + Blob1Name)] Stream inputStream,
            [Blob(ContainerName + "/" + Blob2Name, FileAccess.Write)] Stream outputStream,
            CancellationToken token)
        {
            // Should not be signaled
            if (token.IsCancellationRequested)
            {
                _functionCompletedEvent.Set();
                return;
            }

            await inputStream.CopyToAsync(outputStream);
            outputStream.Close();

            _functionCompletedEvent.Set();
        }

        public static void ReadResultBlob(
            [Blob(ContainerName + "/" + Blob2Name)] string blob,
            CancellationToken token)
        {
            // Should not be signaled
            if (token.IsCancellationRequested)
            {
                return;
            }

            _finalBlobContent = blob;
        }

        private async Task AsyncChainEndToEndInternal()
        {
            JobHost host = new JobHost(_hostConfig);

            await host.StartAsync();
            await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("WriteStartDataMessageToQueue"));

            _functionCompletedEvent.WaitOne();

            // ensure all logs have had a chance to flush
            await Task.Delay(3000);

            // Stop async waits for the function to complete
            await host.StopAsync();

            await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("ReadResultBlob"));
            Assert.Equal("async works", _finalBlobContent);
        }

        private class CustomQueueProcessorFactory : IQueueProcessorFactory
        {
            public List<CustomQueueProcessor> CustomQueueProcessors = new List<CustomQueueProcessor>();

            public QueueProcessor Create(QueueProcessorFactoryContext context)
            {
                // demonstrates how the Queue.ServiceClient options can be configured
                context.Queue.ServiceClient.DefaultRequestOptions.ServerTimeout = TimeSpan.FromSeconds(30);

                // demonstrates how queue options can be customized
                context.Queue.EncodeMessage = true;

                // demonstrates how batch processing behavior can be customized
                context.BatchSize = 30;
                context.NewBatchThreshold = 100;

                CustomQueueProcessor processor = new CustomQueueProcessor(context);
                CustomQueueProcessors.Add(processor);
                return processor;
            }
        }

        public class CustomQueueProcessor : QueueProcessor
        {
            public int BeginProcessingCount = 0;
            public int CompleteProcessingCount = 0;

            public CustomQueueProcessor(QueueProcessorFactoryContext context) : base (context)
            {
                Context = context;
            }

            public QueueProcessorFactoryContext Context { get; private set; }

            public override Task<bool> BeginProcessingMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
            {
                BeginProcessingCount++;
                return base.BeginProcessingMessageAsync(message, cancellationToken);
            }

            public override Task CompleteProcessingMessageAsync(CloudQueueMessage message, FunctionResult result, CancellationToken cancellationToken)
            {
                CompleteProcessingCount++;
                return base.CompleteProcessingMessageAsync(message, result, cancellationToken);
            }

            protected override async Task ReleaseMessageAsync(CloudQueueMessage message, FunctionResult result, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
            {
                // demonstrates how visibility timeout for failed messages can be customized
                // the logic here could implement exponential backoff, etc.
                visibilityTimeout = TimeSpan.FromSeconds(message.DequeueCount);

                await base.ReleaseMessageAsync(message, result, visibilityTimeout, cancellationToken);
            }
        }

        /// <summary>
        /// This custom <see cref="StorageClientFactory"/> demonstrates how clients can be customized.
        /// For example, users can configure global retry policies, DefaultRequestOptions, etc.
        /// </summary>
        public class CustomStorageClientFactory : StorageClientFactory
        {
            public int TotalBlobClientCount;
            public int TotalQueueClientCount;
            public int TotalTableClientCount;

            public int ParameterBlobClientCount;
            public int ParameterQueueClientCount;
            public int ParameterTableClientCount;

            public override CloudBlobClient CreateCloudBlobClient(StorageClientFactoryContext context)
            {
                TotalBlobClientCount++;

                if (context.Parameter != null)
                {
                    ParameterBlobClientCount++;
                }

                return base.CreateCloudBlobClient(context);
            }

            public override CloudQueueClient CreateCloudQueueClient(StorageClientFactoryContext context)
            {
                TotalQueueClientCount++;

                if (context.Parameter != null)
                {
                    ParameterQueueClientCount++;

                    if (context.Parameter.Member.Name == "QueueToQueueAsync")
                    {
                        // demonstrates how context can be used to create a custom client
                        // for a particular method or parameter binding
                    }
                }

                return base.CreateCloudQueueClient(context);
            }

            public override CloudTableClient CreateCloudTableClient(StorageClientFactoryContext context)
            {
                TotalTableClientCount++;

                if (context.Parameter != null)
                {
                    ParameterTableClientCount++;
                }

                return base.CreateCloudTableClient(context);
            }
        }

        public class TestFixture : IDisposable
        {
            public TestFixture()
            {
                JobHostConfiguration config = new JobHostConfiguration();
                StorageAccount = CloudStorageAccount.Parse(config.StorageConnectionString);
            }

            public CloudStorageAccount StorageAccount
            {
                get;
                private set;
            }

            public void Dispose()
            {
                CloudBlobClient blobClient = StorageAccount.CreateCloudBlobClient();
                foreach (var testContainer in blobClient.ListContainers(TestArtifactsPrefix))
                {
                    testContainer.Delete();
                }

                CloudQueueClient queueClient = StorageAccount.CreateCloudQueueClient();
                foreach (var testQueue in queueClient.ListQueues(TestArtifactsPrefix))
                {
                    testQueue.Delete();
                }
            }
        }
    }
}
