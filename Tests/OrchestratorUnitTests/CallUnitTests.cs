using System;
using System.Collections.Generic;
using System.Data.Services.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace OrchestratorUnitTests
{
    [TestClass]
    public class CallUnitTests
    {
        [TestMethod]
        public void InvokeWithParams()
        {            
            var account = TestStorage.GetAccount();
            Utility.DeleteContainer(account, "daas-test-input");

            var l = new ReflectionFunctionInvoker(account, typeof(Program));
            l.Invoke("Chain1", new { inheritedArg = "xyz" }); // blocks

            string log = Program._sb.ToString();
            Assert.AreEqual("1,2,3,4,5,6,7", log);
        }

        class Program
        {
            public static StringBuilder _sb = new StringBuilder();

            [NoAutomaticTrigger]
            public static void Chain1(ICall caller,
                [BlobOutput(@"daas-test-input\test.txt")] TextWriter tw, 
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
                [BlobInput(@"daas-test-input\test.txt")] TextReader tr)
            {
                Assert.AreEqual("abc", arg);

                // Previous func, Chain1, should have flushed writes before we get invoked.
                string content = tr.ReadLine();
                Assert.AreEqual(Message, content);

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
                Assert.AreEqual("def", arg);
                _sb.Append(",7");
            }
        }
    }
}