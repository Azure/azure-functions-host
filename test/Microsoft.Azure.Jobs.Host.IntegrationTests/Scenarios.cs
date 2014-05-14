using System.IO;
using System.Linq;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    public class Scenarios
    {
        // Test basic propagation between blobs. 
        [Fact]
        public void TestQueue()
        {
            var host = new TestJobHost<ProgramQueues>();

            var account = TestStorage.GetAccount();
            string container = @"daas-test-input";
            TestBlobClient.DeleteContainer(account, container);
            QueueClient.DeleteQueue(account, "queuetest");
                        
            TestBlobClient.WriteBlob(account, container, "foo.csv", "15");

            host.Host.RunOneIteration();

            string output = TestBlobClient.ReadBlob(account, container, "foo.output");
            // Ensure blob output has been written
            Assert.NotNull(output);
            Assert.Equal("16", output);

            QueueClient.DeleteQueue(account, "queuetest");
        }

        // Test basic propagation between blobs. 
        [Fact]
        public void TestAggressiveBlobChaining()
        {
            var account = TestStorage.GetAccount();
            var dataConnectionString = account.ToString(true);

            TestJobHostConfiguration configuration = new TestJobHostConfiguration
            {
                StorageValidator = new NullStorageValidator(),
                TypeLocator = new SimpleTypeLocator(typeof(Program)),
                ConnectionStringProvider = new SimpleConnectionStringProvider
                {
                    DataConnectionString = dataConnectionString,
                    RuntimeConnectionString = null
                }
            };

            JobHost host = new JobHost(configuration);
            
            string container = @"daas-test-input";
            TestBlobClient.DeleteContainer(account, container);                       

            // Nothing written yet, so polling shouldn't execute anything
            host.RunOneIteration();

            Assert.False(TestBlobClient.DoesBlobExist(account, container, "foo.2"));
            Assert.False(TestBlobClient.DoesBlobExist(account, container, "foo.3"));

            // Now provide an input and poll again. That should trigger Func1, which produces foo.middle.csv
            TestBlobClient.WriteBlob(account, container, "foo.1", "abc");

            host.RunOneIteration();

            // TODO: do an exponential-backoff retry here to make the tests quick yet robust.
            string middle = TestBlobClient.ReadBlob(account, container, "foo.2");
            // blob should be written
            Assert.NotNull(middle);
            Assert.Equal("foo", middle);

            // The *single* poll a few lines up will cause *both* actions to run as they are chained.
            // this makes sure that our chaining optimization works correctly!
            string output = TestBlobClient.ReadBlob(account, container, "foo.3");
            // blob should be written
            Assert.NotNull(output);
            Assert.Equal("*foo*", output);
        }

        [Fact]
        public void TestQueueToTableEntityWithRouteParameter()
        {
            var account = TestStorage.GetAccount();
            var host = new TestJobHost<ProgramQueues>();

            var queueClient = account.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference("queuetest2");
            string tableName = "tabletest";
            string partitionKey = "PK";
            string rowKey = "RK";
            var tableClient = account.CreateCloudTableClient();
            var table = tableClient.GetTableReference(tableName);

            try
            {
                if (queue.Exists())
                {
                    queue.Delete();
                }
                queue.CreateIfNotExists();
                queue.AddMessage(new CloudQueueMessage(JsonCustom.SerializeObject(new ProgramQueues.TableEntityPayload
                {
                    TableName = tableName,
                    PartitionKey = partitionKey,
                    RowKey = rowKey
                })));
                table.DeleteIfExists();
                table.Create();
                table.Execute(TableOperation.Insert(new SimpleEntity
                {
                    PartitionKey = partitionKey,
                    RowKey = rowKey,
                    Value = 123
                }));

                host.Host.RunOneIteration();

                SimpleEntity entity = (from item in table.CreateQuery<SimpleEntity>()
                                       where item.PartitionKey == partitionKey && item.RowKey == rowKey
                                       select item).FirstOrDefault();
                Assert.Equal(456, entity.Value);
            }
            finally
            {
                if (queue.Exists())
                {
                    queue.Delete();
                }
                table.DeleteIfExists();
            }
        }
    }

 // Blob --> queue message --> Blob
    class ProgramQueues
    {
        public class Payload
        {
            public int Value { get; set; }

            public string Output { get; set; }
        }

        public static void AddToQueue(
            [BlobInput(@"daas-test-input/{name}.csv")] TextReader values, 
            [QueueOutput] out Payload queueTest)
        {
            string content = values.ReadToEnd();
            int val = int.Parse(content);
            queueTest = new Payload { 
                Value = val + 1,
                Output = "foo.output"
            };
        }

        // Triggered on queue. 
        // Route parameters are bound from queue values
        public static void GetFromQueue(
            [QueueInput] Payload queueTest, 
            [BlobOutput(@"daas-test-input/{Output}")] TextWriter output,
            int Value // bound from queueTest.Value
            )
        {
            Assert.Equal(Value, queueTest.Value);
            output.Write(queueTest.Value);
        }

        public class TableEntityPayload
        {
            public string TableName { get; set; }
            public string PartitionKey { get; set; }
            public string RowKey { get; set; }
        }

        public static void BindQueueToTableEntity(
            [QueueInput] TableEntityPayload queueTest2,
            [Table("{TableName}", "{PartitionKey}", "{RowKey}")] SimpleEntity entity)
        {
            entity.Value = 456;
        }
    }

    public class SimpleEntity : TableEntity
    {
        public int Value { get; set; }
    }

    // Test with blobs. {name.csv} --> Func1 --> Func2 --> {name}.middle.csv
    class Program
    {
        public static void Func1(
            [BlobInput(@"daas-test-input/{name}.1")] TextReader values,
            string name,
            [BlobOutput(@"daas-test-input/{name}.2")] TextWriter output)
        {
            output.Write(name);
        }

        public static void Func2(
            [BlobInput(@"daas-test-input/{name}.2")] TextReader values,            
            [BlobOutput(@"daas-test-input/{name}.3")] TextWriter output)
        {
            var content = values.ReadToEnd();

            output.Write("*" + content + "*");
        }
    }


    // Set dev storage. These are well known values.
    class TestStorage
    {
        public static LocalExecutionContext New<T>(CloudStorageAccount account)
        {
            var acs = account.ToString(true);
            var lc = new LocalExecutionContext(acs, typeof(T));
            return lc;
        }

        public static CloudStorageAccount GetAccount()
        {
            return CloudStorageAccount.DevelopmentStorageAccount;
        }
    }
}
