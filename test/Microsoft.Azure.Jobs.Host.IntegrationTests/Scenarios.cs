// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
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
            var host = JobHostFactory.Create<ProgramQueues>();

            var account = TestStorage.GetAccount();
            string container = @"daas-test-input";
            TestBlobClient.DeleteContainer(account, container);
            TestQueueClient.DeleteQueue(account, "queuetest");

            TestBlobClient.WriteBlob(account, container, "foo.csv", "15");

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.CancelAfter(3000);
                ICloudBlob blob = account.CreateCloudBlobClient()
                    .GetContainerReference(container).GetBlockBlobReference("foo.output");
                Action updateTokenSource = () => CancelWhenBlobExists(source, blob);
                RunAndBlock(host, source.Token, updateTokenSource);
            }

            string output = TestBlobClient.ReadBlob(account, container, "foo.output");
            // Ensure blob output has been written
            Assert.NotNull(output);
            Assert.Equal("16", output);

            TestQueueClient.DeleteQueue(account, "queuetest");
        }

        [Fact]
        public void TestPoisonQueue()
        {
            // Arrange
            var account = TestStorage.GetAccount();
            var queue = account.CreateCloudQueueClient().GetQueueReference("bad");
            queue.CreateIfNotExists();
            string expectedMessageText = "fail";

            try
            {
                queue.AddMessage(new CloudQueueMessage(expectedMessageText));
                var host = JobHostFactory.Create<PoisonQueueProgram>();
                host.Start();

                using (CancellationTokenSource source = new CancellationTokenSource())
                {
                    PoisonQueueProgram.SignalOnPoisonMessage = source;

                    // Act
                    source.Token.WaitHandle.WaitOne(3000);

                    // Assert
                    Assert.Equal(expectedMessageText, PoisonQueueProgram.PoisonMessageText);

                    // Cleanup
                    host.Stop();
                }
            }
            finally
            {
                PoisonQueueProgram.SignalOnPoisonMessage = null;
                PoisonQueueProgram.PoisonMessageText = null;
                queue.DeleteIfExists();
            }
        }

        private static void CancelWhenBlobExists(CancellationTokenSource source, ICloudBlob blob)
        {
            if (blob.Exists())
            {
                source.Cancel();
            }
        }

        private static void CancelWhenBlobsExists(CancellationTokenSource source, params ICloudBlob[] blobs)
        {
            if (blobs.All(b => b.Exists()))
            {
                source.Cancel();
            }
        }

        // Test basic propagation between blobs. 
        [Fact]
        public void TestAggressiveBlobChaining()
        {
            var account = TestStorage.GetAccount();
            JobHost host = JobHostFactory.Create<Program>(account);

            string container = @"daas-test-input";
            TestBlobClient.DeleteContainer(account, container);

            // Nothing written yet.
            Assert.False(TestBlobClient.DoesBlobExist(account, container, "foo.2")); // Guard
            Assert.False(TestBlobClient.DoesBlobExist(account, container, "foo.3")); // Guard

            // Now provide an input and poll again. That should trigger Func1, which produces foo.middle.csv
            TestBlobClient.WriteBlob(account, container, "foo.1", "abc");

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.CancelAfter(3000);
                CloudBlobContainer containerReference =
                    account.CreateCloudBlobClient().GetContainerReference(container);
                ICloudBlob middleBlob = containerReference.GetBlockBlobReference("foo.2");
                ICloudBlob outputBlob = containerReference.GetBlockBlobReference("foo.3");
                Action updateTokenSource = () => CancelWhenBlobsExists(source, middleBlob, outputBlob);
                RunAndBlock(host, source.Token, updateTokenSource);
            }

            // TODO: do an exponential-backoff retry here to make the tests quick yet robust.
            string middle = TestBlobClient.ReadBlob(account, container, "foo.2");
            // blob should be written
            Assert.NotNull(middle);
            Assert.Equal("foo", middle);

            // The polling a few lines up waits for *both* actions to run as they are chained.
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
            var host = JobHostFactory.Create<ProgramQueues>();

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

                using (CancellationTokenSource source = new CancellationTokenSource())
                {
                    source.CancelAfter(3000);
                    Action updateTokenSource = () => CancelWhenRowUpdated<SimpleEntity>(source, table, partitionKey, rowKey,
                        (current) => current.Value == 456);
                    RunAndBlock(host, source.Token, updateTokenSource);
                }

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

        private static void RunAndBlock(JobHost host, CancellationToken cancellationToken, Action pollAction)
        {
            cancellationToken.Register(host.Stop);
            Thread pollThread = new Thread(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    pollAction();

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Thread.Sleep(2 * 1000);
                    }
                }
            });
            host.RunAndBlock();
        }

        private static void CancelWhenRowUpdated<TElement>(CancellationTokenSource source, CloudTable table,
            string partitionKey, string rowKey, Func<TElement, bool> watcher) where TElement : ITableEntity
        {
            TableOperation retrieve = TableOperation.Retrieve<TElement>(partitionKey, rowKey);
            TableResult result = table.Execute(retrieve);
            if (watcher.Invoke((TElement)result.Result))
            {
                source.Cancel();
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
            [BlobTrigger(@"daas-test-input/{name}.csv")] TextReader values,
            [Queue("queueTest")] out Payload queueTest)
        {
            string content = values.ReadToEnd();
            int val = int.Parse(content);
            queueTest = new Payload
            {
                Value = val + 1,
                Output = "foo.output"
            };
        }

        // Triggered on queue. 
        // Route parameters are bound from queue values
        public static void GetFromQueue(
            [QueueTrigger("queueTest")] Payload queueTest,
            [Blob(@"daas-test-input/{Output}")] TextWriter output,
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
            [QueueTrigger("queueTest2")] TableEntityPayload queueTest2,
            [Table("{TableName}", "{PartitionKey}", "{RowKey}")] SimpleEntity entity)
        {
            entity.Value = 456;
        }
    }

    public class PoisonQueueProgram
    {
        public static CancellationTokenSource SignalOnPoisonMessage { get; set; }

        public static string PoisonMessageText { get; set; }

        public static void PutInPoisonQueue([QueueTrigger("bad")] string message)
        {
            throw new InvalidOperationException();
        }

        public static void ReceiveFromPoisonQueue([QueueTrigger("bad-poison")] string message)
        {
            PoisonMessageText = message;
            CancellationTokenSource source = SignalOnPoisonMessage;

            if (source != null)
            {
                source.Cancel();
            }
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
            [BlobTrigger(@"daas-test-input/{name}.1")] TextReader values,
            string name,
            [Blob(@"daas-test-input/{name}.2")] TextWriter output)
        {
            output.Write(name);
        }

        public static void Func2(
            [BlobTrigger(@"daas-test-input/{name}.2")] TextReader values,
            [Blob(@"daas-test-input/{name}.3")] TextWriter output)
        {
            var content = values.ReadToEnd();

            output.Write("*" + content + "*");
        }
    }

    // Set dev storage. These are well known values.
    class TestStorage
    {
        public static LocalExecutionContext New<T>(CloudStorageAccount account, Type[] cloudBlobStreamBinderTypes = null)
        {
            return new LocalExecutionContext(typeof(T), account, cloudBlobStreamBinderTypes);
        }

        public static CloudStorageAccount GetAccount()
        {
            return CloudStorageAccount.DevelopmentStorageAccount;
        }
    }
}
