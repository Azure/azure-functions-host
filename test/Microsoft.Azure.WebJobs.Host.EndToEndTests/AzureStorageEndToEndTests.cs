// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    /// <summary>
    /// Various E2E tests that use only the public surface and the real Azure storage
    /// </summary>
    public class AzureStorageEndToEndTests : IClassFixture<AzureStorageEndToEndTests.TestFixture>
    {
        private const string TestArtifactsPrefix = "e2etest";
        private const string ContainerName = TestArtifactsPrefix + "container%rnd%";
        private const string BlobName = "testblob";

        private const string TableName = TestArtifactsPrefix + "table%rnd%";

        private const string HostStartQueueName = TestArtifactsPrefix + "startqueue%rnd%";
        private const string TestQueueName = TestArtifactsPrefix + "queue%rnd%";
        private const string TestQueueNameEtag = TestArtifactsPrefix + "etag2equeue%rnd%";
        private const string DoneQueueName = TestArtifactsPrefix + "donequeue%rnd%";

        private const string BadMessageQueue1 = TestArtifactsPrefix + "-badmessage1-%rnd%";
        private const string BadMessageQueue2 = TestArtifactsPrefix + "-badmessage2-%rnd%";

        private static int _badMessage1Calls;
        private static int _badMessage2Calls;

        private static EventWaitHandle _startWaitHandle;
        private static EventWaitHandle _functionChainWaitHandle;
        private CloudStorageAccount _storageAccount;
        private RandomNameResolver _resolver;
        private static object TestResult;

        private static string _lastMessageId;
        private static string _lastMessagePopReceipt;

        public AzureStorageEndToEndTests(TestFixture fixture)
        {
            _storageAccount = fixture.StorageAccount;
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

        /// <summary>
        /// We'll insert a bad message. It should get here okay. It will
        /// then pass it on to the next trigger.
        /// </summary>
        public static void BadMessage_CloudQueueMessage(
            [QueueTrigger(BadMessageQueue1)] CloudQueueMessage badMessageIn,
            [Queue(BadMessageQueue2)] out CloudQueueMessage badMessageOut,
            TraceWriter log)
        {
            _badMessage1Calls++;
            badMessageOut = badMessageIn;
        }

        public static void BadMessage_String(
            [QueueTrigger(BadMessageQueue2)] string message,
            TraceWriter log)
        {
            _badMessage2Calls++;
        }

        [NoAutomaticTrigger]
        public static void TableWithFilter(
            [QueueTrigger("test")] Person person,
            [Table(TableName, Filter = "(Age gt {Age}) and (Location eq '{Location}')")] JArray results)
        {
            TestResult = results;
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

        [Fact]
        public async Task TableFilterTest()
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

            // write test entities
            string testTableName = _resolver.ResolveInString(TableName);
            CloudTableClient tableClient = _storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference(testTableName);
            await table.CreateIfNotExistsAsync();
            var operation = new TableBatchOperation();
            operation.Insert(new Person
            {
                PartitionKey = "1",
                RowKey = "1",
                Name = "Lary",
                Age = 20,
                Location = "Seattle"
            });
            operation.Insert(new Person
            {
                PartitionKey = "1",
                RowKey = "2",
                Name = "Moe",
                Age = 35,
                Location = "Seattle"
            });
            operation.Insert(new Person
            {
                PartitionKey = "1",
                RowKey = "3",
                Name = "Curly",
                Age = 45,
                Location = "Texas"
            });
            operation.Insert(new Person
            {
                PartitionKey = "1",
                RowKey = "4",
                Name = "Bill",
                Age = 28,
                Location = "Tam O'Shanter"
            });
            await table.ExecuteBatchAsync(operation);

            JobHost host = new JobHost(hostConfig);
            var methodInfo = this.GetType().GetMethod("TableWithFilter", BindingFlags.Public | BindingFlags.Static);
            var input = new Person { Age = 25, Location = "Seattle" };
            string json = JsonConvert.SerializeObject(input);
            var arguments = new { person = json };
            await host.CallAsync(methodInfo, arguments);

            // wait for test results to appear
            await TestHelpers.Await(() => TestResult != null);

            JArray results = (JArray)TestResult;
            Assert.Equal(1, results.Count);

            input = new Person { Age = 25, Location = "Tam O'Shanter" };
            json = JsonConvert.SerializeObject(input);
            arguments = new { person = json };
            await host.CallAsync(methodInfo, arguments);
            await TestHelpers.Await(() => TestResult != null);
            results = (JArray)TestResult;
            Assert.Equal(1, results.Count);
            Assert.Equal("Bill", (string)results[0]["Name"]);
        }

        private void EndToEndTest(bool uploadBlobBeforeHostStart)
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

        [Fact]
        public async Task BadQueueMessageE2ETests()
        {
            // This test ensures that the host does not crash on a bad message (it previously did)
            // Insert a bad message into a queue that should:
            // - trigger BadMessage_CloudQueueMessage, which will put it into a second queue that will
            // - trigger BadMessage_String, which should fail
            // - BadMessage_String should fail repeatedly until it is moved to the poison queue
            // The test will watch that poison queue to know when to complete

            // Reinitialize the name resolver to avoid conflicts
            _resolver = new RandomNameResolver();

            JobHostConfiguration hostConfig = new JobHostConfiguration()
            {
                NameResolver = _resolver,
                TypeLocator = new FakeTypeLocator(
                    this.GetType(),
                    typeof(BlobToCustomObjectBinder))
            };

            // use a custom processor so we can grab the Id and PopReceipt
            hostConfig.Queues.QueueProcessorFactory = new TestQueueProcessorFactory();

            var tracer = new TestTraceWriter(TraceLevel.Verbose);
            hostConfig.Tracing.Tracers.Add(tracer);

            ILoggerFactory loggerFactory = new LoggerFactory();
            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);
            hostConfig.LoggerFactory = loggerFactory;

            // The jobs host is started
            JobHost host = new JobHost(hostConfig);
            host.Start();

            // use reflection to construct a bad message:
            // - use a GUID as the content, which is not a valid base64 string
            // - pass 'true', to indicate that it is a base64 string
            string messageContent = Guid.NewGuid().ToString();
            object[] parameters = new object[] { messageContent, true };
            CloudQueueMessage message = Activator.CreateInstance(typeof(CloudQueueMessage),
                BindingFlags.Instance | BindingFlags.NonPublic, null, parameters, null) as CloudQueueMessage;

            var queueClient = _storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference(_resolver.ResolveInString(BadMessageQueue1));
            await queue.CreateIfNotExistsAsync();
            await queue.ClearAsync();

            // the poison queue will end up off of the second queue
            var poisonQueue = queueClient.GetQueueReference(_resolver.ResolveInString(BadMessageQueue2) + "-poison");
            await poisonQueue.DeleteIfExistsAsync();

            await queue.AddMessageAsync(message);

            CloudQueueMessage poisonMessage = null;
            await TestHelpers.Await(() =>
            {
                bool done = false;
                if (poisonQueue.Exists())
                {
                    poisonMessage = poisonQueue.GetMessage();
                    done = poisonMessage != null;

                    if (done)
                    {
                        // Sleep briefly, then make sure the other message has been deleted.
                        // If so, trying to delete it again will throw an error.
                        Thread.Sleep(1000);

                        // The message is in the second queue
                        var queue2 = queueClient.GetQueueReference(_resolver.ResolveInString(BadMessageQueue2));

                        StorageException ex = Assert.Throws<StorageException>(
                            () => queue2.DeleteMessage(_lastMessageId, _lastMessagePopReceipt));
                        Assert.Equal("MessageNotFound", ex.RequestInformation.ExtendedErrorInformation.ErrorCode);
                    }
                }
                return done;
            });

            host.Stop();

            // find the raw string to compare it to the original
            Assert.NotNull(poisonMessage);
            var propInfo = typeof(CloudQueueMessage).GetProperty("RawString", BindingFlags.Instance | BindingFlags.NonPublic);
            string rawString = propInfo.GetValue(poisonMessage) as string;
            Assert.Equal(messageContent, rawString);

            // Make sure the functions were called correctly
            Assert.Equal(1, _badMessage1Calls);
            Assert.Equal(0, _badMessage2Calls);

            // make sure the exception is being properly logged
            var errors = tracer.Traces.Where(t => t.Level == TraceLevel.Error);
            Assert.True(errors.All(t => t.Exception.InnerException.InnerException is FormatException));

            // Validate Logger
            var loggerErrors = loggerProvider.GetAllLogMessages().Where(l => l.Level == Extensions.Logging.LogLevel.Error);
            Assert.True(loggerErrors.All(t => t.Exception.InnerException.InnerException is FormatException));
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

        public class Person : TableEntity
        {
            public int Age { get; set; }
            public string Location { get; set; }
            public string Name { get; set; }
        }

        private class TestQueueProcessorFactory : IQueueProcessorFactory
        {
            public QueueProcessor Create(QueueProcessorFactoryContext context)
            {
                return new TestQueueProcessor(context);
            }
        }

        private class TestQueueProcessor : QueueProcessor
        {
            public TestQueueProcessor(QueueProcessorFactoryContext context)
                : base(context)
            {
            }

            public override Task<bool> BeginProcessingMessageAsync(CloudQueueMessage message, CancellationToken cancellationToken)
            {
                _lastMessageId = message.Id;
                _lastMessagePopReceipt = message.PopReceipt;

                return base.BeginProcessingMessageAsync(message, cancellationToken);
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

                CloudTableClient tableClient = StorageAccount.CreateCloudTableClient();
                foreach (var testTable in tableClient.ListTables(TestArtifactsPrefix))
                {
                    testTable.Delete();
                }
            }
        }
    }
}
