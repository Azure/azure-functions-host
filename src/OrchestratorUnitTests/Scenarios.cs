using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;




namespace Microsoft.WindowsAzure.JobsUnitTests
{
    [TestClass]
    public class Scenarios
    {
        // Test basic propagation between blobs. 
        [TestMethod]
        public void TestQueue()
        {
            var account = TestStorage.GetAccount();
            string container = @"daas-test-input";
            Utility.DeleteContainer(account, container);
            Utility.DeleteQueue(account, "queuetest");

            var w = LocalOrchestrator.Build(account, typeof(ProgramQueues));

            Utility.WriteBlob(account, container, "foo.csv", "15");

            // Unspecified the exact number of polls it will take to push through both functions. 
            // Should be less than total length of functions to call.
            for (int i = 0; i < 3; i++)
            {
                w.Poll();
            }

            string output = Utility.ReadBlob(account, container, "foo.output");
            Assert.IsNotNull(output, "blob should have been written");
            Assert.AreEqual("16", output);

            Utility.DeleteQueue(account, "queuetest");
        }

        // Test basic propagation between blobs. 
        [TestMethod]
        public void Test()
        {
            var account = TestStorage.GetAccount();
            string container = @"daas-test-input";
            Utility.DeleteContainer(account, container);

            var w = LocalOrchestrator.Build(account, typeof(Program));
            
            // Nothing written yet, so polling shouldn't execute anything
            w.Poll(); 
            Assert.IsFalse(Utility.DoesBlobExist(account, container, "foo.middle.csv"));
            Assert.IsFalse(Utility.DoesBlobExist(account, container, "foo.output.csv"));

            // Now provide an input and poll again. That should trigger Func1, which produces foo.middle.csv
            Thread.Sleep(3000); // $$$ Why are these sleeps needed?
            Utility.WriteBlob(account, container, "foo.csv", "abc");
            Thread.Sleep(3000);

            w.Poll();

            string middle = Utility.ReadBlob(account, container, "foo.middle.csv");
            Assert.IsNotNull(middle, "blob should have been written");

            Assert.AreEqual("foo", middle);

            // Orchestrator propagation is still deterministic, so we know this isn't written yet.
            // If this fails, it's not so bad. It means the orchestrator is propagating faster than expected.
            // It could be the result of some optimization.
            Assert.IsFalse(Utility.DoesBlobExist(account, container, "foo.output.csv"), "output shouldn't be written yet");
            
            // Now poll again. Should trigger func2
            w.Poll();

            string output = Utility.ReadBlob(account, container, "foo.output.csv");
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
        }

        public static void AddToQueue([BlobInput(@"daas-test-input\{name}.csv")] TextReader values, [QueueOutput] out Payload queueTest)
        {
            string content = values.ReadToEnd();
            int val = int.Parse(content);
            queueTest = new Payload { Value = val + 1 };
        }

        public static void GetFromQueue([QueueInput] Payload queueTest, [BlobOutput(@"daas-test-input\foo.output")] TextWriter output)
        {
            output.Write(queueTest.Value);
        }
    }

    // Test with blobs. {name.csv} --> Func1 --> Func2 --> {name}.middle.csv
    class Program
    {
        public static void Func1(
            [BlobInput(@"daas-test-input\{name}.csv")] TextReader values,
            string name,
            [BlobOutput(@"daas-test-input\{name}.middle.csv")] TextWriter output)
        {
            output.Write(name);
        }

        public static void Func2(
            [BlobInput(@"daas-test-input\{name}.middle.csv")] TextReader values,            
            [BlobOutput(@"daas-test-input\{name}.output.csv")] TextWriter output)
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
