using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
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