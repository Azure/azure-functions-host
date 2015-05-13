// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    /// <summary>
    /// Various E2E tests that use only the public surface and the real Azure storage
    /// </summary>
    public class AzureStorageEndToEndTests
    {
        private const string ContainerName = "e2econtainer%rnd%";
        private const string BlobName = "testblob";

        private const string TableName = "e2etable%rnd%";

        private const string HostStartQueueName = "e2estart%rnd%";
        private const string TestQueueName = "e2equeue%rnd%";
        private const string TestQueueNameEtag = "etag2equeue%rnd%";
        private const string DoneQueueName = "e2edone%rnd%";

        private static EventWaitHandle _startWaitHandle;

        private static EventWaitHandle _functionChainWaitHandle;

        private CloudStorageAccount _storageAccount;

        private RandomNameResolver _resolver;

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
            [BlobTrigger(ContainerName + @"/{name}")] CustomObject input,
            string name,
            [Queue(TestQueueNameEtag)] out CustomObject output)
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
        public static void QueueToICollectorAndQueue(
            [QueueTrigger(TestQueueNameEtag)] CustomObject e2equeue,
            [Table(TableName)] ICollector<ITableEntity> table,
            [Queue(TestQueueName)] out CustomObject output)
        {
            const string tableKeys = "testETag";

            DynamicTableEntity result = new DynamicTableEntity
            {
                PartitionKey = tableKeys,
                RowKey = tableKeys,
                Properties = new Dictionary<string, EntityProperty>()
                {
                    { "Text", new EntityProperty("before") },
                    { "Number", new EntityProperty("1") }
                }
            };

            table.Add(result);

            result.Properties["Text"] = new EntityProperty("after");
            result.ETag = "*";
            table.Add(result);

            output = e2equeue;
        }

        /// <summary>
        /// Covers:
        /// - queue binding to custom object
        /// - queue trigger
        /// - table writing
        /// </summary>
        public static void QueueToTable(
            [QueueTrigger(TestQueueName)] CustomObject e2equeue,
            [Table(TableName)] CloudTable table,
            [Queue(DoneQueueName)] out string e2edone)
        {
            const string tableKeys = "test";

            CustomTableEntity result = new CustomTableEntity
            {
                PartitionKey = tableKeys,
                RowKey = tableKeys,
                Text = e2equeue.Text + " " + "QueueToTable",
                Number = e2equeue.Number + 1
            };

            table.Execute(TableOperation.InsertOrReplace(result));

            // Write a queue message to signal the scenario completion
            e2edone = "done";
        }

        /// <summary>
        /// Notifies the completion of the scenario
        /// </summary>
        public static void NotifyCompletion(
            [QueueTrigger(DoneQueueName)] string e2edone)
        {
            _functionChainWaitHandle.Set();
        }

        // Uncomment the Fact attribute to run
        // [Fact(Timeout = 20 * 60 * 1000)]
        public void AzureStorageEndToEndSlow()
        {
            EndToEndTest(uploadBlobBeforeHostStart: false);
        }

        [Fact]
        public void AzureStorageEndToEndFast()
        {
            EndToEndTest(uploadBlobBeforeHostStart: true);
        }

        private void EndToEndTest(bool uploadBlobBeforeHostStart)
        {
            try
            {
                EndToEndTestInternal(uploadBlobBeforeHostStart);
            }
            finally
            {
                CleanupBlob();
                CleanupQueue();
                CleanupTable();
            }
        }

        private void EndToEndTestInternal(bool uploadBlobBeforeHostStart)
        {
            // Reinitialize the name resolver to avoid conflicts
            _resolver = new RandomNameResolver();

            JobHostConfiguration hostConfig = new JobHostConfiguration()
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(
                    this.GetType(),
                    typeof(BlobToCustomObjectBinder))
            };

            _storageAccount = CloudStorageAccount.Parse(hostConfig.StorageConnectionString);

            if (uploadBlobBeforeHostStart)
            {
                // The function will be triggered fast because the blob is already there
                UploadTestObject();
            }

            // The jobs host is started
            JobHost host = new JobHost(hostConfig);

            _functionChainWaitHandle = new ManualResetEvent(initialState: false);

            host.Start();

            if (!uploadBlobBeforeHostStart)
            {
                WaitForTestFunctionsToStart();
                UploadTestObject();
            }

            bool signaled = _functionChainWaitHandle.WaitOne(15 * 60 * 1000);

            // Stop the host and wait for it to finish
            host.Stop();

            Assert.True(signaled);

            // Verify
            VerifyTableResults();
        }

        private void UploadTestObject()
        {
            string testContainerName = _resolver.ResolveInString(ContainerName);

            CloudBlobContainer container = _storageAccount.CreateCloudBlobClient().GetContainerReference(testContainerName);
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

            string startQueueName = _resolver.ResolveInString(HostStartQueueName);

            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(startQueueName);
            queue.CreateIfNotExists();
            queue.AddMessage(new CloudQueueMessage(String.Empty));

            _startWaitHandle.WaitOne();
        }

        private void CleanupBlob()
        {
            string testContainerName = _resolver.ResolveInString(ContainerName);

            CloudBlobClient blobClient = _storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(testContainerName);
            container.DeleteIfExists();
        }

        private void CleanupQueue()
        {
            string testQueueName = _resolver.ResolveInString(TestQueueName);

            CloudQueueClient queueClient = _storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference(testQueueName);
            queue.DeleteIfExists();
        }

        private void CleanupTable()
        {
            string testTableName = _resolver.ResolveInString(TableName);

            CloudTableClient tableClient = _storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(testTableName);
            table.DeleteIfExists();
        }

        private void VerifyTableResults()
        {
            string testTableName = _resolver.ResolveInString(TableName);

            CloudTableClient tableClient = _storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(testTableName);

            Assert.True(table.Exists(), "Result table not found");

            TableQuery query = new TableQuery()
                .Where(TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "test"),
                    TableOperators.And,
                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, "test")))
                .Take(1);
            DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

            // Ensure expected row found
            Assert.NotNull(result);

            Assert.Equal("Test testblob QueueToTable", result.Properties["Text"].StringValue);
            Assert.Equal(44, result.Properties["Number"].Int32Value);

            query = new TableQuery()
                .Where(TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "testETag"),
                    TableOperators.And,
                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, "testETag")))
                .Take(1);
            result = table.ExecuteQuery(query).FirstOrDefault();

            // Ensure expected row found
            Assert.NotNull(result);

            Assert.Equal("after", result.Properties["Text"].StringValue);
        }

        private class CustomTableEntity : TableEntity
        {
            public string Text { get; set; }

            public int Number { get; set; }
        }
    }
}
