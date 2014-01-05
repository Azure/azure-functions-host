using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Test;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    [TestClass]
    public class Scenarios
    {
        // Test basic propagation between blobs. 
        [TestMethod]
        public void TestQueue()
        {
            var host = new TestJobHost<ProgramQueues>();

            var account = TestStorage.GetAccount();
            string container = @"daas-test-input";
            BlobClient.DeleteContainer(account, container);
            QueueClient.DeleteQueue(account, "queuetest");
                        
            BlobClient.WriteBlob(account, container, "foo.csv", "15");

            host.Host.RunOneIteration();

            string output = BlobClient.ReadBlob(account, container, "foo.output");
            Assert.IsNotNull(output, "blob should have been written");
            Assert.AreEqual("16", output);

            QueueClient.DeleteQueue(account, "queuetest");
        }

        // Test basic propagation between blobs. 
        [TestMethod]
        public void TestAggressiveBlobChaining()
        {
            var account = TestStorage.GetAccount();
            var acs = account.ToString(true);

            JobHost host = new JobHost(acs, null, new JobHostTestHooks
            {
                 StorageValidator = new NullStorageValidator(),
                 TypeLocator = new SimpleTypeLocator(typeof(Program))
            });
            
            string container = @"daas-test-input";
            BlobClient.DeleteContainer(account, container);                       

            // Nothing written yet, so polling shouldn't execute anything
            host.RunOneIteration();

            Assert.IsFalse(BlobClient.DoesBlobExist(account, container, "foo.2"));
            Assert.IsFalse(BlobClient.DoesBlobExist(account, container, "foo.3"));

            // Now provide an input and poll again. That should trigger Func1, which produces foo.middle.csv
            BlobClient.WriteBlob(account, container, "foo.1", "abc");

            host.RunOneIteration();

            // TODO: do an exponential-backoff retry here to make the tests quick yet robust.
            string middle = BlobClient.ReadBlob(account, container, "foo.2");
            Assert.IsNotNull(middle, "blob should have been written");
            Assert.AreEqual("foo", middle);

            // The *single* poll a few lines up will cause *both* actions to run as they are chained.
            // this makes sure that our chaining optimization works correctly!
            string output = BlobClient.ReadBlob(account, container, "foo.3");
            Assert.IsNotNull(output, "blob should have been written");
            Assert.AreEqual("*foo*", output);
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
            Assert.AreEqual(Value, queueTest.Value);
            output.Write(queueTest.Value);
        }
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
