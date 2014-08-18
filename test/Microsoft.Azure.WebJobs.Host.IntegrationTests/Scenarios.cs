// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public class Scenarios
    {
        // Test basic propagation between blobs. 
        [Fact]
        public void TestQueue()
        {
            // Arrange
            using (var host = JobHostFactory.Create<ProgramQueues>())
            {
                var account = TestStorage.GetAccount();
                string container = @"daas-test-input";
                TestBlobClient.DeleteContainer(account, container);
                TestQueueClient.DeleteQueue(account, "queuetest");

                TestBlobClient.WriteBlob(account, container, "foo.csv", "15");
                ICloudBlob blob = account.CreateCloudBlobClient()
                    .GetContainerReference(container).GetBlockBlobReference("foo.output");

                host.Start();

                // Act
                Wait(3 * 1000, () => DoesBlobExist(blob));

                // Assert
                string output = TestBlobClient.ReadBlob(account, container, "foo.output");
                // Ensure blob output has been written
                Assert.NotNull(output);
                Assert.Equal("16", output);

                TestQueueClient.DeleteQueue(account, "queuetest");

                // Cleanup
                host.Stop();
            }
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

                using (CancellationTokenSource source = new CancellationTokenSource())
                using (JobHost host = JobHostFactory.Create<PoisonQueueProgram>())
                {
                    PoisonQueueProgram.SignalOnPoisonMessage = source;

                    host.Start();

                    // Act
                    bool poisonMessageReceived = source.Token.WaitHandle.WaitOne(3000);

                    // Assert
                    Assert.True(poisonMessageReceived); // Guard
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

        [Fact]
        public void TestMaxDequeueCount()
        {
            // Arrange
            var account = TestStorage.GetAccount();
            var queue = account.CreateCloudQueueClient().GetQueueReference("bad");
            queue.CreateIfNotExists();
            int expectedDequeueCount = 7;

            try
            {
                queue.AddMessage(new CloudQueueMessage("fail"));

                using (CancellationTokenSource source = new CancellationTokenSource())
                using (JobHost host = JobHostFactory.Create<MaxDequeueCountProgram>(maxDequeueCount: expectedDequeueCount))
                {
                    MaxDequeueCountProgram.SignalOnPoisonMessage = source;

                    host.Start();

                    // Act
                    bool poisonMessageReceived = source.Token.WaitHandle.WaitOne(3000);

                    // Assert
                    Assert.True(poisonMessageReceived); // Guard
                    Assert.Equal(expectedDequeueCount, MaxDequeueCountProgram.DequeueCount);

                    // Cleanup
                    host.Stop();
                }
            }
            finally
            {
                MaxDequeueCountProgram.SignalOnPoisonMessage = null;
                MaxDequeueCountProgram.DequeueCount = 0;
                queue.DeleteIfExists();
            }
        }

        private static bool DoesBlobExist(ICloudBlob blob)
        {
            return blob.Exists();
        }

        private static bool DoBlobsExist(params ICloudBlob[] blobs)
        {
            return blobs.All(b => b.Exists());
        }

        // Test basic propagation between blobs. 
        [Fact]
        public void TestAggressiveBlobChaining()
        {
            // Arrange
            var account = TestStorage.GetAccount();
            string container = @"daas-test-input";

            using (JobHost host = JobHostFactory.Create<Program>(account))
            {
                TestBlobClient.DeleteContainer(account, container);

                // Nothing written yet.
                Assert.False(TestBlobClient.DoesBlobExist(account, container, "foo.2")); // Guard
                Assert.False(TestBlobClient.DoesBlobExist(account, container, "foo.3")); // Guard

                // Now provide an input and poll again. That should trigger Func1, which produces foo.middle.csv
                TestBlobClient.WriteBlob(account, container, "foo.1", "abc");

                CloudBlobContainer containerReference =
                    account.CreateCloudBlobClient().GetContainerReference(container);
                ICloudBlob middleBlob = containerReference.GetBlockBlobReference("foo.2");
                ICloudBlob outputBlob = containerReference.GetBlockBlobReference("foo.3");

                host.Start();

                // Act
                Wait(3 * 1000, () => DoBlobsExist(middleBlob, outputBlob));

                // Assert
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

                // Cleanup
                host.Stop();
            }
        }

        [Fact]
        public void TestQueueToTableEntityWithRouteParameter()
        {
            var account = TestStorage.GetAccount();

            using (JobHost host = JobHostFactory.Create<ProgramQueues>())
            {
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
                    queue.AddMessage(new CloudQueueMessage(JsonCustom.SerializeObject(
                        new ProgramQueues.TableEntityPayload
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

                    host.Start();

                    // Act
                    Wait(3 * 1000, () => IsRowUpdated<SimpleEntity>(table, partitionKey, rowKey, (current) =>
                        current.Value == 456));

                    // Assert
                    SimpleEntity entity = (from item in table.CreateQuery<SimpleEntity>()
                                           where item.PartitionKey == partitionKey && item.RowKey == rowKey
                                           select item).FirstOrDefault();
                    Assert.Equal(456, entity.Value);

                    // Cleanup
                    host.Stop();
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

        private static void Wait(int millisecondsTimeout, Func<bool> completed)
        {
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.CancelAfter(millisecondsTimeout);

                Thread pollUntilCanceledThread = new Thread(() =>
                {
                    while (!source.IsCancellationRequested)
                    {
                        if (completed.Invoke())
                        {
                            source.Cancel();
                        }

                        if (!source.IsCancellationRequested)
                        {
                            Thread.Sleep(1 * 1000);
                        }
                    }
                });

                pollUntilCanceledThread.Start();
                pollUntilCanceledThread.Join();
            }
        }

        private static bool IsRowUpdated<TElement>(CloudTable table, string partitionKey, string rowKey,
            Func<TElement, bool> watcher) where TElement : ITableEntity
        {
            TableOperation retrieve = TableOperation.Retrieve<TElement>(partitionKey, rowKey);
            TableResult result = table.Execute(retrieve);
            return watcher.Invoke((TElement)result.Result);
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

    public class MaxDequeueCountProgram
    {
        public static CancellationTokenSource SignalOnPoisonMessage { get; set; }

        public static int DequeueCount { get; set; }

        public static void PutInPoisonQueue([QueueTrigger("bad")] string message)
        {
            DequeueCount++;
            throw new InvalidOperationException();
        }

        public static void ReceiveFromPoisonQueue([QueueTrigger("bad-poison")] string message)
        {
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
