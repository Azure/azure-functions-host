using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.EndToEndTests
{
    /// <summary>
    /// Various E2E tests that use only the public surface and the real Azure storage
    /// </summary>
    public class EndToEndTests
    {
        private const string ContainerName = "e2econtainer";
        private const string BlobName = "testblob";

        private const string TableName = "e2etable";

        private const string HostStartQueueName = "e2estart";
        private const string TestQueueName = "e2equeue";
        private const string DoneQueueName = "e2edone";

        private static EventWaitHandle _startWaitHandle;

        private static EventWaitHandle _functionChainWaitHandle;

        private CloudStorageAccount _storageAccount;

        private string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="EndToEndTests"/> class.
        /// </summary>
        public EndToEndTests()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["Test"].ConnectionString;

            try
            {
                _storageAccount = CloudStorageAccount.Parse(_connectionString);
            }
            catch (Exception ex)
            {
                throw new FormatException("The connection string in App.config is invalid", ex);
            }

            CleanupBlob();
            CleanupQueue();
            CleanupTable();
        }

        /// <summary>
        /// Used to syncronize the application start and blob creation
        /// </summary>
        public static void NotifyStart(
            [QueueTrigger(HostStartQueueName)] string input)
        {
            _startWaitHandle.Set();
        }

        /// <summary>
        /// Covers:
        /// - blob binding to custom object
        /// - blob trigger
        /// - queue writing
        /// - blob name pattern binding
        /// </summary>
        public static void BlobToQueue(
            [BlobInput(ContainerName + @"/{name}")] CustomObject input,
            string name,
            [QueueOutput(TestQueueName)] out CustomObject output)
        {
            CustomObject result = new CustomObject()
            {
                Text = input.Text + " " + name,
                Number = input.Number + 1
            };

            output = result;
        }

        /// <summary>
        /// Covers:
        /// - queue binding to custom object
        /// - queue trigger
        /// - table writing
        /// </summary>
        public static void QueueToTable(
            [QueueTrigger(TestQueueName)] CustomObject e2equeue,
            [Table(TableName)] IDictionary<Tuple<string, string>, CustomObject> table,
            [QueueOutput] out string e2edone)
        {
            const string tableKeys = "test";

            CustomObject result = new CustomObject()
            {
                Text = e2equeue.Text + " " + "QueueToTable",
                Number = e2equeue.Number + 1
            };

            table.Add(new Tuple<string, string>(tableKeys, tableKeys), result);

            // Write a queue message to signal the scenario completion
            e2edone = "done";
        }

        /// <summary>
        /// Notifies the completion of the scenario
        /// </summary>
        public static void NotifyCompletion(
            [QueueTrigger("e2edone")] string e2edone)
        {
            _functionChainWaitHandle.Set();
        }

        // Switch the Fact attribute to run
        [Fact(Skip = "Slow test with 15 minutes timeout")]
        //[Fact(Timeout = 15 * 60 * 1000)]
        public void EndToEndSlowTrigger()
        {
            EndToEndTest(uploadBlobBeforeHostStart: false);
        }

        // 1 minute timeout
        [Fact(Timeout = 60 * 1000)]
        public void EndToEndFastTrigger()
        {
            EndToEndTest(uploadBlobBeforeHostStart: true);
        }

        private void EndToEndTest(bool uploadBlobBeforeHostStart)
        {
            if (uploadBlobBeforeHostStart)
            {
                // The function will be triggered fast because the blob is already there
                UploadTestObject();
            }

            // The jobs host is started
            JobHost host = new JobHost(new JobHostConfiguration(_connectionString));

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            _functionChainWaitHandle = new ManualResetEvent(initialState: false);

            Thread hostThread = new Thread(() => host.RunAndBlock(tokenSource.Token));
            hostThread.Start();

            if (!uploadBlobBeforeHostStart)
            {
                WaitForTestFunctionsToStart();
                UploadTestObject();
            }

            _functionChainWaitHandle.WaitOne();

            // Stop the host
            tokenSource.Cancel();

            // Verify
            VerifyTableResults();

            // Kill the host thread if it is still running
            if (hostThread.IsAlive)
            {
                hostThread.Abort();
            }
        }


        private void UploadTestObject()
        {
            CloudBlobContainer container = _storageAccount.CreateCloudBlobClient().GetContainerReference(ContainerName);
            container.CreateIfNotExists();

            // The test blob
            CloudBlockBlob testBlob = container.GetBlockBlobReference(BlobName);
            CustomObject testObject = new CustomObject()
            {
                Text = "Test",
                Number = 42
            };

            testBlob.UploadText(JsonConvert.SerializeObject(testObject));
        }

        private void WaitForTestFunctionsToStart()
        {
            _startWaitHandle = new ManualResetEvent(initialState: false);

            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(HostStartQueueName);
            queue.CreateIfNotExists();
            queue.AddMessage(new CloudQueueMessage(String.Empty));

            _startWaitHandle.WaitOne();
        }

        private void CleanupBlob()
        {
            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();

            // Cleanup the items in container, queue and table rather than deleting and recreating because
            // it is faster and there are no creation conflicts.

            // Cleanup the blob storage
            CloudBlobContainer container = blobClient.GetContainerReference(ContainerName);
            if (container.Exists())
            {
                // Remove all the blobs from the container (if any)
                IEnumerable<IListBlobItem> blobs = container.ListBlobs();
                if (blobs != null)
                {
                    foreach (ICloudBlob blob in blobs.OfType<ICloudBlob>())
                    {
                        blob.Delete();
                    }
                }
            }
            else
            {
                container.Create();
            }
        }

        private void CleanupQueue()
        {
            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(TestQueueName);
            if (queue.Exists())
            {
                queue.Clear();
            }
            queue = queueClient.GetQueueReference(DoneQueueName);
            if (queue.Exists())
            {
                queue.Clear();
            }
        }

        private void CleanupTable()
        {
            CloudTableClient tableClient = _storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(TableName);

            if (table.Exists())
            {
                TableQuery query = new TableQuery();
                var result = table.ExecuteQuery(query);

                if (result != null)
                {
                    foreach (var entity in result)
                    {
                        table.Execute(TableOperation.Delete(entity));
                    }
                }
            }
        }

        private void VerifyTableResults()
        {
            CloudTableClient tableClient = _storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(TableName);

            Assert.True(table.Exists(), "Result table not found");

            TableQuery query = new TableQuery().Take(1);
            DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

            // Ensure expected row found
            Assert.NotNull(result);

            Assert.Equal("Test testblob QueueToTable", result.Properties["Text"].StringValue);
            Assert.Equal("44", result.Properties["Number"].StringValue);
        }
    }
}
