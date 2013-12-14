using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;
using System.Reflection;


using System.IO;

namespace Microsoft.WindowsAzure.JobsUnitTests
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

            var lc = TestStorage.New<Program>(account);
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