using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RunnerInterfaces;
using Microsoft.WindowsAzure.StorageClient;
using System.Reflection;
using Orchestrator;
using SimpleBatch;
using System.IO;

namespace OrchestratorUnitTests
{
    /// <summary>
    /// Summary description for ModelbindingTests
    /// </summary>
    [TestClass]
    public class ContextBinderTests
    {
        [TestMethod]
        public void TestBindToIBinder()
        {
            var account = TestStorage.GetAccount();

            var lc = new LocalExecutionContext(account, typeof(Program));
            Guid gActual = lc.Call("Test");

            Assert.AreNotEqual(gActual, Guid.Empty);            
            Assert.AreEqual(gActual, Program._executed);
        }

        class Program
        {
            public static Guid _executed;

            [NoAutomaticTrigger]
            public static void Test(IContext context)
            {
                _executed = context.FunctionInstanceGuid;
            }
        }
    }    
}