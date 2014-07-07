using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    public class LocalOrchestratorTests
    {
        [Fact]
        public void InvokeWithParams()
        {
            var account = TestStorage.GetAccount();

            TestBlobClient.DeleteContainer(account, "daas-test-input");
            TestBlobClient.WriteBlob(account, "daas-test-input", "note-monday.csv", "abc");

            var d = new Dictionary<string, object>() {
                { "values", "daas-test-input/note-monday.csv" },
                { "unbound", "test" },
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithNames", d);

            string content = TestBlobClient.ReadBlob(account, "daas-test-input", "note.csv");
            Assert.Equal("done", content);

            {
                // $$$ Put this in its own unit test?
                var blob = account.CreateCloudBlobClient().GetContainerReference("daas-test-input").GetBlockBlobReference("note.csv");
                var guid = BlobCausalityManager.GetWriter(blob);

                Assert.True(guid != Guid.Empty, "Blob is missing causality information");
            }
        }

        [Fact]
        public void InvokeWithBlob()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, "daas-test-input");
            TestBlobClient.WriteBlob(account, "daas-test-input", "blob.csv", "0,1,2");

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithBlob");
        }

        // Test binding blobs to strings. 
        [Fact]
        public void InvokeWithBlobToString()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, "daas-test-input");

            string value = "abc";
            TestBlobClient.WriteBlob(account, "daas-test-input", "blob.txt", value);

            var lc = TestStorage.New<Program>(account);
            lc.Call("BindBlobToString");

            string content = TestBlobClient.ReadBlob(account, "daas-test-input", "blob.out");
            Assert.Equal(value, content);
        }

        [Fact]
        public void InvokeWithMissingBlobBlockBlob()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, "daas-test-input");

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithMissingBlobBlockBlob");
        }

        [Fact]
        public void InvokeWithMissingBlobStream()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, "daas-test-input");

            var lc = TestStorage.New<Program>(account);
            // Not found
            Assert.Throws<InvalidOperationException>(() => lc.Call("FuncWithMissingBlobStream"));
        }

        [Fact]
        public void InvokeWithMissingBlobTextReader()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, "daas-test-input");

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithMissingBlobTextReader");
        }

        [Fact]
        public void InvokeWithParamsParser()
        {
            var account = TestStorage.GetAccount();
            TestBlobClient.DeleteContainer(account, "daas-test-input");

            var d = new Dictionary<string, object>() {
                { "x", "15" },
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("ParseArgument", d);

            string content = TestBlobClient.ReadBlob(account, "daas-test-input", "out.csv");
            Assert.Equal("16", content);
        }

        [Fact]
        public void InvokeOnBlob()
        {
            var account = TestStorage.GetAccount();

            TestBlobClient.WriteBlob(account, "daas-test-input", "input.csv", "abc");

            var lc = TestStorage.New<Program>(account);
            // TODO: Remove second parameter once host.Call supports more flexibility.
            lc.CallOnBlob("Func1", "daas-test-input/input.csv");

            string content = TestBlobClient.ReadBlob(account, "daas-test-input", "input.csv");

            Assert.Equal("abc", content);
        }

        // Test binding a parameter to the CloudStorageAccount that a function is uploaded to. 
        [Fact]
        public void TestBindCloudStorageAccount()
        {
            var account = TestStorage.GetAccount();

            var args = new
            {
                containerName = "daas-test-input",
                blobName = "input.txt",
                value = "abc"
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncCloudStorageAccount", args);

            string content = TestBlobClient.ReadBlob(account, args.containerName, args.blobName);
            Assert.Equal(args.value, content);
        }

        [Fact]
        public void TestEnqueueMessage()
        {
            var account = TestStorage.GetAccount();

            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference("myoutputqueue");
            TestQueueClient.DeleteQueue(queue);

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncEnqueue");

            var msg = queue.GetMessage();
            Assert.NotNull(msg);

            string data = msg.AsString;
            Payload payload = JsonConvert.DeserializeObject<Payload>(data);

            Assert.Equal(15, payload.Value);
        }

        [Fact]
        public void TestMultiEnqueueMessage_IEnumerable()
        {
            var account = TestStorage.GetAccount();

            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference("myoutputqueue");
            TestQueueClient.DeleteQueue(queue);

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncMultiEnqueueICollection");

            for (int i = 10; i <= 30; i += 10)
            {
                var msg = queue.GetMessage();
                Assert.NotNull(msg);

                string data = msg.AsString;
                queue.DeleteMessage(msg);

                Payload payload = JsonConvert.DeserializeObject<Payload>(data);

                // Technically, ordering is not gauranteed.
                Assert.Equal(i, payload.Value);
            }

            {
                var msg = queue.GetMessage();
                Assert.Null(msg); // no more messages
            }
        }

        [Fact]
        public void TestEnqueueMessage_UsingCloudQueue()
        {
            var account = TestStorage.GetAccount();

            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference("myoutputqueue");
            TestQueueClient.DeleteQueue(queue);

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncCloudQueueEnqueue");

            for (int i = 10; i <= 30; i += 10)
            {
                var msg = queue.GetMessage();
                Assert.NotNull(msg);

                string data = msg.AsString;
                queue.DeleteMessage(msg);

                Assert.Equal(i.ToString(), data);
            }

            {
                var msg = queue.GetMessage();
                Assert.Null(msg); // no more messages
            }
        }

        private class Program
        {
            // This can be invoked explicitly (and providing parameters)
            // or it can be invoked implicitly by triggering on input. // (assuming no unbound parameters)
            [NoAutomaticTrigger]
            public static void FuncWithNames(
                string name, string date,  // used by input
                string unbound, // not used by in/out
                [BlobTrigger(@"daas-test-input/{name}-{date}.csv")] TextReader values,
                [Blob(@"daas-test-input/{name}.csv")] TextWriter output
                )
            {
                Assert.Equal("test", unbound);
                Assert.Equal("note", name);
                Assert.Equal("monday", date);

                string content = values.ReadToEnd();
                Assert.Equal("abc", content);

                output.Write("done");
            }

            public static void BindBlobToString(
                [Blob(@"daas-test-input/blob.txt")] string blobIn,
                [Blob(@"daas-test-input/blob.out")] out string blobOut
                )
            {
                blobOut = blobIn;
            }

            public static void FuncWithBlob(
                [Blob(@"daas-test-input/blob.csv")] CloudBlockBlob blob,
                [Blob(@"daas-test-input/blob.csv")] Stream stream
                )
            {
                Assert.NotNull(blob);
                Assert.NotNull(stream);

                Assert.Equal("blob.csv", blob.Name);
                string content = blob.DownloadText();
                Assert.NotNull(content);
                string[] strings = content.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                // Verify expected number of entries in CloudBlob
                Assert.Equal(3, strings.Length);
                for (int i = 0; i < 3; ++i)
                {
                    int value;
                    bool parsed = int.TryParse(strings[i], out value);
                    string message = String.Format("Unable to parse CloudBlob strings[{0}]: '{1}'", i, strings[i]);
                    Assert.True(parsed, message);
                    // Ensure expected value in CloudBlob
                    Assert.Equal(i, value);
                }

                Assert.True(stream.CanRead, "Unable to read stream");
                StreamReader reader = new StreamReader(stream);
                content = reader.ReadToEnd();
                Assert.NotNull(content);
                strings = content.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                // Verify expected number of entries in Stream
                Assert.Equal(3, strings.Length);
                for (int i = 0; i < 3; ++i)
                {
                    int value;
                    bool parsed = int.TryParse(strings[i], out value);
                    string message = String.Format("Unable to parse Stream strings[{0}]: '{1}'", i, strings[i]);
                    Assert.True(parsed, message);
                    // Ensure expected value in Stream
                    Assert.Equal(i, value);
                }
            }

            public static void FuncWithMissingBlobBlockBlob([Blob(@"daas-test-input/blob.csv")] CloudBlockBlob blob)
            {
                Assert.NotNull(blob);
                Assert.Equal("blob.csv", blob.Name);
                Assert.Equal("daas-test-input", blob.Container.Name);
            }

            public static void FuncWithMissingBlobStream([Blob(@"daas-test-input/blob.csv")] Stream stream)
            {
                throw new InvalidOperationException();
            }

            public static void FuncWithMissingBlobTextReader([Blob(@"daas-test-input/blob.csv")] TextReader reader)
            {
                Assert.Null(reader);
            }

            public static void ParseArgument(int x, [Blob(@"daas-test-input/out.csv")] TextWriter output)
            {
                output.Write(x + 1);
            }

            public static void Func1(
                [BlobTrigger(@"daas-test-input/input.csv")] TextReader values,
                [Blob(@"daas-test-input/output.csv")] TextWriter output)
            {
                string content = values.ReadToEnd();
                output.Write(content);
            }

            public static void FuncEnqueue(
                [Queue("myoutputqueue")] out Payload myoutputqueue)
            {
                // Send payload to Azure queue "myoutputqueue"
                myoutputqueue = new Payload { Value = 15 };
            }

            public static void FuncMultiEnqueueICollection(
                [Queue("myoutputqueue")] ICollection<Payload> myoutputqueue)
            {
                myoutputqueue.Add(new Payload { Value = 10 });
                myoutputqueue.Add(new Payload { Value = 20 });
                myoutputqueue.Add(new Payload { Value = 30 });
            }

            public static void FuncCloudQueueEnqueue(
                [Queue("myoutputqueue")]CloudQueue queue)
            {
                queue.AddMessage(new CloudQueueMessage("10"));
                queue.AddMessage(new CloudQueueMessage("20"));
                queue.AddMessage(new CloudQueueMessage("30"));
            }

            // Test binding to CloudStorageAccount 
            [NoAutomaticTrigger]
            public static void FuncCloudStorageAccount(CloudStorageAccount account, string value, string containerName, string blobName)
            {
                TestBlobClient.WriteBlob(account, containerName, blobName, value);
            }
        }

        private class Payload
        {
            public int Value { get; set; }
        }
    }
}
