using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    public class CallUnitTests
    {
        // Verify that ICall doesn't invoke if the parent method failed. 
        [Fact]
        public void InvokeFail()
        {
            var account = TestStorage.GetAccount();
            var lc = TestStorage.New<ProgramFail>(account);
            ICall call = lc;
            var guid1 = call.QueueCall("Method").Guid;

            var lookup = lc.FunctionInstanceLookup;

            // Verify Method1 failed
            var log1 = lookup.LookupOrThrow(guid1);
            Assert.Equal(FunctionInstanceStatus.CompletedFailed, log1.GetStatus());
            Assert.Equal("System.InvalidOperationException", log1.ExceptionType);
            Assert.Equal(ProgramFail.Message, log1.ExceptionMessage);

            Assert.False(ProgramFail._isCalled, "Method2 should not be invoked since Method1 failed");
        }

        class ProgramFail
        {
            public const string Message = "Failed!";

            [NoAutomaticTrigger]
            public static void Method(ICall call)
            {
                call.QueueCall("Method2"); // shouldn't be called

                throw new InvalidOperationException(Message);
            }


            public static bool _isCalled = false;

            [NoAutomaticTrigger]
            public static void Method2()
            {
                _isCalled = true;
                // This should not be called
            }
        }

        [Fact]
        public void InvokeChain()
        {            
            var account = TestStorage.GetAccount();
            BlobClient.DeleteContainer(account, "daas-test-input");
            Program._sb.Clear();

            var lc = TestStorage.New<Program>(account);
            ICall call = lc;
            var guid1 = call.QueueCall("Chain1", new { inheritedArg = "xyz" }).Guid;

            string log = Program._sb.ToString();
            Assert.Equal("1,2,3,4,5,6,7", log);

            // Verify the causality chain
            var causality = lc.CausalityReader;
            var lookup = lc.FunctionInstanceLookup;

            // Chain1
            var log1 = lookup.LookupOrThrow(guid1);            
            Assert.Equal("Chain1", GetMethodName(log1));
            Assert.Equal(FunctionInstanceStatus.CompletedSuccess, log1.GetStatus());
            var children = causality.GetChildren(guid1).ToArray();
            Assert.Equal(1, children.Length);
            Assert.Equal(guid1, children[0].ParentGuid); // call to Child2
            var guid2 = children[0].ChildGuid;

            // Chain2
            var log2 = lookup.LookupOrThrow(guid2);
            Assert.Equal("Chain2", GetMethodName(log2));
            Assert.Equal(FunctionInstanceStatus.CompletedSuccess, log2.GetStatus());
            children = causality.GetChildren(guid2).ToArray();
            Assert.Equal(1, children.Length);
            Assert.Equal(guid2, children[0].ParentGuid); // Call to Child3
            var guid3 = children[0].ChildGuid;

            // Chain3
            var log3 = lookup.LookupOrThrow(guid3);
            Assert.Equal("Chain3", GetMethodName(log3));
            Assert.Equal(FunctionInstanceStatus.CompletedSuccess, log3.GetStatus());
            children = causality.GetChildren(guid3).ToArray();
            Assert.Equal(0, children.Length);
        }

        static string GetMethodName(ExecutionInstanceLogEntity log)
        {
            var x = (MethodInfoFunctionLocation) log.FunctionInstance.Location;
            var mi = x.MethodInfo;
            return mi.Name;
        }

        class Program
        {
            public static StringBuilder _sb = new StringBuilder();

            [NoAutomaticTrigger]
            public static void Chain1(ICall caller,
                [BlobOutput(@"daas-test-input/test.txt")] TextWriter tw, 
                string inheritedArg)
            {
                _sb.Append("1");

                var  d = new Dictionary<string, string>()
                {
                    { "arg", "abc" }
                };
                caller.QueueCall("Chain2", d); // deferred
                d["arg"] = "failed"; // test mutating args. Should use copy of args at time of invoke.
                                
                _sb.Append(",2");
                // Shouldn't run yet. Pause to sniff out a possible race. 
                Thread.Sleep(1000);

                tw.Write(Message); // side-effect

                _sb.Append(",3");
            }

            const string Message = "abc";

            [NoAutomaticTrigger]
            public static void Chain2(ICall caller, 
                string arg, 
                [BlobInput(@"daas-test-input/test.txt")] TextReader tr)
            {
                Assert.Equal("abc", arg);

                // Previous func, Chain1, should have flushed writes before we get invoked.
                string content = tr.ReadLine();
                Assert.Equal(Message, content);

                _sb.Append(",4");
                caller.QueueCall("Chain3", new { arg = "def" }); 

                Console.WriteLine("new arg:{0}", arg);
                _sb.Append(",5");
            }

            [NoAutomaticTrigger]
            public static void Chain3(string arg)
            {
                _sb.Append(",6");
                Console.WriteLine("new arg:{0}", arg);
                Assert.Equal("def", arg);
                _sb.Append(",7");
            }
        }


        [Fact]
        public void InvokeDelete()
        {
            // Test invoking a delete operation 
            var account = TestStorage.GetAccount();
            BlobClient.DeleteContainer(account, "daas-test-input");
            BlobClient.DeleteContainer(account, "daas-test-archive");

            BlobClient.WriteBlob(account, "daas-test-input", "foo-input.txt", "12");

            var l = TestStorage.New<Program2>(account);
            l.Call("Chain1", new { name = "foo" }); // blocks

            Assert.False(BlobClient.DoesBlobExist(account, "daas-test-input", "foo-input.txt"), "Blob should have been archived");
            
            string content = BlobClient.ReadBlob(account, "daas-test-input", "foo-output.txt");
            Assert.Equal("13", content); // ouput

            string content2 = BlobClient.ReadBlob(account, "daas-test-archive", "foo-input.txt");
            Assert.Equal("12", content2); // archive of input
        }

        class Program2
        {
            [NoAutomaticTrigger]
            public static void Chain1(
                [BlobOutput(@"daas-test-input/{name}-input.txt")] TextReader tr,
                [BlobOutput(@"daas-test-input/{name}-output.txt")] TextWriter tw,
                string name,
                ICall caller)
            {
                int i = int.Parse(tr.ReadToEnd());
                tw.Write(i+1);

                caller.QueueCall("ArchiveInput", new { name = name });
            }

            // Move a blob out of the listening folder and into an archive folder
            [NoAutomaticTrigger]
            public static void ArchiveInput(
                [BlobOutput(@"daas-test-input/{name}-input.txt")] CloudBlob original,
                [BlobOutput(@"daas-test-archive/{name}-input.txt")] CloudBlob archive
                )
            {
                archive.CopyFromBlob(original); // blocks           
                original.Delete();
            }
        }
    }
}
