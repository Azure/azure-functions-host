using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    public class LocalOrchestratorTests
    {
        [Fact]
        public void InvokeWithParams()
        {
            var account = TestStorage.GetAccount();

            BlobClient.DeleteContainer(account, "daas-test-input");
            BlobClient.WriteBlob(account, "daas-test-input", "note-monday.csv", "abc");
                        
            var d = new Dictionary<string, string>() {
                { "name", "note" },
                { "date" , "monday" },
                { "unbound", "test" },
                { "target", "out"}
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithNames", d);

            string content = BlobClient.ReadBlob(account, "daas-test-input", "out.csv");
            Assert.Equal("done", content);

            {
                // $$$ Put this in its own unit test?
                IBlobCausalityLogger logger = new BlobCausalityLogger();
                var blob = BlobClient.GetBlob(account, "daas-test-input", "out.csv");
                var guid = logger.GetWriter(blob);

                Assert.True(guid != Guid.Empty, "Blob is missing causality information");
            }
        }

        [Fact]
        public void InvokeWithBlob()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            BlobClient.DeleteContainer(account, "daas-test-input");
            BlobClient.WriteBlob(account, "daas-test-input", "blob.csv", "0,1,2");

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithBlob");
        }

        // Test binding blobs to strings. 
        [Fact]
        public void InvokeWithBlobToString()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            BlobClient.DeleteContainer(account, "daas-test-input");

            string value = "abc";
            BlobClient.WriteBlob(account, "daas-test-input", "blob.txt", value);

            var lc = TestStorage.New<Program>(account);
            lc.Call("BindBlobToString");

            string content = BlobClient.ReadBlob(account, "daas-test-input", "blob.out");
            Assert.Equal(value, content);
        }

        [Fact]
        public void InvokeWithMissingBlob()
        {
            CloudStorageAccount account = TestStorage.GetAccount();
            BlobClient.DeleteContainer(account, "daas-test-input");

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncWithMissingBlob");
        }

        [Fact]
        public void InvokeWithParamsParser()
        {
            var account = TestStorage.GetAccount();
            BlobClient.DeleteContainer(account, "daas-test-input");

            var d = new Dictionary<string, string>() {
                { "x", "15" },
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("ParseArgument", d);

            string content = BlobClient.ReadBlob(account, "daas-test-input", "out.csv");
            Assert.Equal("16", content);
        }

        [Fact]
        public void InvokeOnBlob()
        {
            var account = TestStorage.GetAccount();

            BlobClient.WriteBlob(account, "daas-test-input", "input.csv", "abc");

            var lc = TestStorage.New<Program>(account);
            lc.Call("Func1");

            string content = BlobClient.ReadBlob(account, "daas-test-input", "input.csv");

            Assert.Equal("abc", content);
        }

        [Fact(Skip = "enable the test when we support ref (with proper lease)")]
        public void TestBlobLease()
        {
            // Test that we get the blob lease, write while holding the lease. 
            // We don't need a lease to read. 
            BlobParameterRuntimeBinding.BlobLeaseTestHook = () => new MockBlobLeaseHolder();

            var account = TestStorage.GetAccount();

            string Container = "daas-test-input";
            string BlobName = "counter.txt";

            var blob = BlobClient.GetBlob(account, Container, BlobName);
            blob.DeleteIfExists();

            var blobLease = MockBlobLeaseHolder.GetBlobSuffix(blob, ".lease");
            blob.DeleteIfExists(); // get into a known state

            var lc = TestStorage.New<Program>(account);
            lc.Call("BlobLease"); // Invoke once, will create file
                        
            string content = blob.DownloadText();
            Assert.Equal("1", content);

            string content2 = BlobClient.ReadBlob(account, Container, BlobName + ".x");
            // Ensure wrote while holding the lease
            Assert.Equal(content, content2);

            lc.Call("BlobLease"); // Invoke second time. 
                        
            string content3 = blob.DownloadText();
            Assert.Equal("2", content3);

            Assert.False(BlobClient.DoesBlobExist(blobLease), "Blob lease was not released");
        }

        // Test binding a parameter to the CloudStorageAccount that a function is uploaded to. 
        [Fact]
        public void TestBindCloudStorageAccount()
        {
            var account = TestStorage.GetAccount();

            var args = new {
                containerName = "daas-test-input",
                blobName = "input.txt",
                value = "abc"
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncCloudStorageAccount", args);            

            string content = BlobClient.ReadBlob(account, args.containerName, args.blobName);
            Assert.Equal(args.value, content);
        }

        [Fact]
        public void TestEnqueueMessage()
        {
            var account = TestStorage.GetAccount();

            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference("myoutputqueue");
            QueueClient.DeleteQueue(queue);

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
            QueueClient.DeleteQueue(queue);

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncMultiEnqueueIEnumerable");

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
        public void TestMultiEnqueueMessage_NestedIEnumerable_Throws()
        {
            var account = TestStorage.GetAccount();

            var lc = TestStorage.New<Program>(account);
            CallError<InvalidOperationException>(lc, "FuncMultiEnqueueIEnumerableNested");
        }

        [Fact]
        public void TestQueueOutput_IList_Throws()
        {
            var account = TestStorage.GetAccount();

            var lc = TestStorage.New<Program>(account);
            CallError<InvalidOperationException>(lc, "FuncQueueOutputIList");
        }

        [Fact]
        public void TestQueueOutput_Object_Throws()
        {
            var account = TestStorage.GetAccount();

            var lc = TestStorage.New<Program>(account);
            CallError<InvalidOperationException>(lc, "FuncQueueOutputObject");
        }

        [Fact]
        public void TestQueueOutput_IEnumerableOfObject_Throws()
        {
            var account = TestStorage.GetAccount();

            var lc = TestStorage.New<Program>(account);
            CallError<InvalidOperationException>(lc, "FuncQueueOutputIEnumerableOfObject");
        }

        static void CallError<T>(LocalExecutionContext lc, string functionName) where T : Exception
        {
            var guid = lc.Call("FuncQueueOutputIEnumerableOfObject");

            var lookup = lc.FunctionInstanceLookup;

            var log1 = lookup.LookupOrThrow(guid);
            Assert.Equal(FunctionInstanceStatus.CompletedFailed, log1.GetStatus());
            Assert.Equal(typeof(T).FullName, log1.ExceptionType);
        }


        // Model bind a parameter to a queueing inteface, and then enqueue multiple messages.
        [Fact]
        public void TestMultiEnqueueMessage()
        {
            var account = TestStorage.GetAccount();

            CloudQueue queue = account.CreateCloudQueueClient().GetQueueReference("myoutputqueue");
            QueueClient.DeleteQueue(queue);

            var lc = TestStorage.New<Program>(account);
            lc.Call("FuncMultiEnqueue");

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
            QueueClient.DeleteQueue(queue);

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

        // $$$ Test with directories
        [Fact]
        public void Aggregate()
        {
            var account = TestStorage.GetAccount();

            BlobClient.DeleteContainer(account, "daas-test-input");
            BlobClient.WriteBlob(account, "daas-test-input", "1.csv", "abc");
            BlobClient.WriteBlob(account, "daas-test-input", "2.csv", "def");

            var lc = TestStorage.New<Program>(account);
            lc.Call("Aggregate1");

            string content = BlobClient.ReadBlob(account, "daas-test-input", "output.csv");
            Assert.Equal("abcdef", content);
        }

        [Fact]
        public void Aggregate2()
        {
            var account = TestStorage.GetAccount();

            BlobClient.DeleteContainer(account, "daas-test-input");
            BlobClient.WriteBlob(account, "daas-test-input", @"test/1.csv", "abc");
            BlobClient.WriteBlob(account, "daas-test-input", @"test/2.csv", "def");

            var d = new Dictionary<string, string>() {
                { "deployId", "test" },
                { "outdir", "testoutput" }
            };

            var lc = TestStorage.New<Program>(account);
            lc.Call("Aggregate2", d);

            string content = BlobClient.ReadBlob(account, "daas-test-input", @"testoutput/output.csv");
            Assert.Equal("abcdef", content);
        }

        private class Program
        {
            public static void BlobLease([BlobInput(@"daas-test-input/counter.txt")]  ref int x)
            {
                x++;
            }

            // This can be invoked explicitly (and providing parameters)
            // or it can be invoked implicitly by triggering on input. // (assuming no unbound parameters)
            public static void FuncWithNames(
                string name, string date,  // used by input
                string unbound, // not used by in/out
                string target, // only used by output
                [BlobInput(@"daas-test-input/{name}-{date}.csv")] TextReader values,
                [BlobOutput(@"daas-test-input/{target}.csv")] TextWriter output
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
                [BlobInput(@"daas-test-input/blob.txt")] string blobIn,
                [BlobOutput(@"daas-test-input/blob.out")] out string blobOut
                )
            {
                blobOut = blobIn;
            }

            [NoAutomaticTrigger]
            public static void FuncWithBlob(
                [BlobInput(@"daas-test-input/blob.csv")] CloudBlob blob,
                [BlobInput(@"daas-test-input/blob.csv")] Stream stream
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

            [NoAutomaticTrigger]
            public static void FuncWithMissingBlob(
                [BlobInput(@"daas-test-input/blob.csv")] CloudBlob blob,
                [BlobInput(@"daas-test-input/blob.csv")] Stream stream,
                [BlobInput(@"daas-test-input/blob.csv")] TextReader reader
                )
            {
                Assert.Null(blob);
                Assert.Null(stream);
                Assert.Null(reader);
            }

            [NoAutomaticTrigger]
            public static void ParseArgument(int x, [BlobOutput(@"daas-test-input/out.csv")] TextWriter output)
            {
                output.Write(x + 1);
            }

            public static void Func1(
                [BlobInput(@"daas-test-input/input.csv")] TextReader values,
                [BlobOutput(@"daas-test-input/output.csv")] TextWriter output)
            {
                string content = values.ReadToEnd();
                output.Write(content);
            }

            public static void FuncEnqueue(
                [QueueOutput] out Payload myoutputqueue)
            {
                // Send payload to Azure queue "myoutputqueue"
                myoutputqueue = new Payload { Value = 15 };
            }

            public static void FuncMultiEnqueueIEnumerable(
                [QueueOutput] out IEnumerable<Payload> myoutputqueue)
            {
                List<Payload> payloads = new List<Payload>();
                payloads.Add(new Payload { Value = 10 });
                payloads.Add(new Payload { Value = 20 });
                payloads.Add(new Payload { Value = 30 });
                myoutputqueue = payloads;
            }

            public static void FuncMultiEnqueueIEnumerableNested(
                [QueueOutput] out IEnumerable<IEnumerable<Payload>> myoutputqueue)
            {
                List<IEnumerable<Payload>> payloads = new List<IEnumerable<Payload>>();
                myoutputqueue = payloads;
            }

            public static void FuncQueueOutputIEnumerableOfObject(
                [QueueOutput] out IEnumerable<object> myoutputqueue)
            {
                List<object> payloads = new List<object>();
                myoutputqueue = payloads;
            }

            public static void FuncQueueOutputObject(
                [QueueOutput] out object myoutputqueue)
            {
                List<IEnumerable<Payload>> payloads = new List<IEnumerable<Payload>>();
                myoutputqueue = payloads;
            }

            public static void FuncQueueOutputIList(
                [QueueOutput] out IList<Payload> myoutputqueue)
            {
                List<Payload> payloads = new List<Payload>();
                myoutputqueue = payloads;
            }

            [Jobs.Description("cloud queue function")] // needed for indexing since we have no other attrs
            public static void FuncCloudQueueEnqueue(
                CloudQueue myoutputqueue)
            {
                myoutputqueue.AddMessage(new CloudQueueMessage("10"));
                myoutputqueue.AddMessage(new CloudQueueMessage("20"));
                myoutputqueue.AddMessage(new CloudQueueMessage("30"));
            }

            [Jobs.Description("queue function")] // needed for indexing since we have no other attrs
            public static void FuncMultiEnqueue(
               IQueueOutput<Payload> myoutputqueue)
            {
                myoutputqueue.Add(new Payload { Value = 10 });
                myoutputqueue.Add(new Payload { Value = 20 });
                myoutputqueue.Add(new Payload { Value = 30 });
            }

            // Test binding to CloudStorageAccount 
            [Jobs.Description("test")]
            public static void FuncCloudStorageAccount(CloudStorageAccount account, string value, string containerName, string blobName)
            {
                BlobClient.WriteBlob(account, containerName, blobName, value);
            }
        }

        private class Payload
        {
            public int Value { get; set; }
        }
    }
}
