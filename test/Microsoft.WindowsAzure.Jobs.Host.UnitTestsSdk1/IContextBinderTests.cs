using System;
using Microsoft.WindowsAzure.Jobs;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    /// <summary>
    /// Summary description for ModelbindingTests
    /// </summary>
    public class ContextBinderTests
    {
        [Fact]
        public void TestBindToIBinder()
        {
            var account = TestStorage.GetAccount();

            var lc = TestStorage.New<Program>(account);
            Guid gActual = lc.Call("Test");

            Assert.NotEqual(gActual, Guid.Empty);            
            Assert.Equal(gActual, Program._executed);
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
