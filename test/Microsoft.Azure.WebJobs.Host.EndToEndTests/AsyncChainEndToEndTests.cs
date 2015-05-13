// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class AsyncChainEndToEndTests
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

        private static EventWaitHandle _functionCompletedEvent;

        private static string _finalBlobContent;

        [NoAutomaticTrigger]
        public static void WriteStartDataMessageToQueue(
            [Queue(Queue1Name)] ICollector<string> queueMessages,
            [Blob(ContainerName + "/" + NonWebJobsBlobName, FileAccess.Write)] Stream nonSdkBlob,
            CancellationToken token)
        {
            queueMessages.Add(" works");

            byte[] messageBytes = Encoding.UTF8.GetBytes("async");
            nonSdkBlob.Write(messageBytes, 0, messageBytes.Length);
        }

        public static async Task QueueToQueueAsync(
            [QueueTrigger(Queue1Name)] string message,
            [Queue(Queue2Name)] IAsyncCollector<string> output,
            CancellationToken token)
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(_resolver.ResolveInString(ContainerName));
            CloudBlockBlob blob = container.GetBlockBlobReference(NonWebJobsBlobName);
            string blobContent = await blob.DownloadTextAsync();

            await output.AddAsync(blobContent + message);
        }

        public static async Task QueueToBlobAsync(
            [QueueTrigger(Queue2Name)] string message,
            [Blob(ContainerName + "/" + Blob1Name, FileAccess.Write)] Stream blobStream,
            CancellationToken token)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

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

        [Fact]
        public async Task AsyncChainEndToEnd()
        {
            try
            {
                using (_functionCompletedEvent = new ManualResetEvent(initialState: false))
                {
                    await AsyncChainEndToEndInternal();
                }
            }
            finally
            {
                Cleanup();
            }
        }

        private async Task AsyncChainEndToEndInternal()
        {
            StringWriter consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);

            _resolver = new RandomNameResolver();
 
            JobHostConfiguration hostConfiguration = new JobHostConfiguration()
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(typeof(AsyncChainEndToEndTests))
            };

            _storageAccount = CloudStorageAccount.Parse(hostConfiguration.StorageConnectionString);

            JobHost host = new JobHost(hostConfiguration);

            await host.StartAsync();
            await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("WriteStartDataMessageToQueue"));

            _functionCompletedEvent.WaitOne();

            // Stop async waits for the function to complete
            await host.StopAsync();

            await host.CallAsync(typeof(AsyncChainEndToEndTests).GetMethod("ReadResultBlob"));
            Assert.Equal("async works", _finalBlobContent);

            string firstQueueName = _resolver.ResolveInString(Queue1Name);
            string secondQueueName = _resolver.ResolveInString(Queue2Name);
            string blobContainerName = _resolver.ResolveInString(ContainerName);
            string[] consoleOutputLines = consoleOutput.ToString().Trim().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            string[] expectedOutputLines = new string[]
                {
                    "Found the following functions:",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.WriteStartDataMessageToQueue",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueToQueueAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.QueueToBlobAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.BlobToBlobAsync",
                    "Microsoft.Azure.WebJobs.Host.EndToEndTests.AsyncChainEndToEndTests.ReadResultBlob",
                    "Job host started",
                    "Executing: 'AsyncChainEndToEndTests.WriteStartDataMessageToQueue' - Reason: 'This was function was programmatically called via the host APIs.'",
                    string.Format("Executing: 'AsyncChainEndToEndTests.QueueToQueueAsync' - Reason: 'New queue message detected on '{0}'.'", firstQueueName),
                    string.Format("Executing: 'AsyncChainEndToEndTests.QueueToBlobAsync' - Reason: 'New queue message detected on '{0}'.'", secondQueueName),
                    string.Format("Executing: 'AsyncChainEndToEndTests.BlobToBlobAsync' - Reason: 'New blob detected: {0}/Blob1'", blobContainerName),
                    "Job host stopped",
                    "Executing: 'AsyncChainEndToEndTests.ReadResultBlob' - Reason: 'This was function was programmatically called via the host APIs.'"
                };
            Assert.True(consoleOutputLines.OrderBy(p => p).SequenceEqual(expectedOutputLines.OrderBy(p => p)));
        }

        private void Cleanup()
        {
            if (_storageAccount != null)
            {
                CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
                blobClient
                    .GetContainerReference(_resolver.ResolveInString(ContainerName))
                    .DeleteIfExists();

                CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
                queueClient
                    .GetQueueReference(_resolver.ResolveInString(Queue1Name))
                    .DeleteIfExists();
                queueClient
                    .GetQueueReference(_resolver.ResolveInString(Queue2Name))
                    .DeleteIfExists();
            }
        }
    }
}
