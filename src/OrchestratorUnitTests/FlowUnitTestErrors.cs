using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Microsoft.WindowsAzure.JobsUnitTests
{
    // Test failure cases for indexing
    [TestClass]
    public class FlowUnitTestErrors
    {
        [TestMethod]
        public void TestFails()
        {
            foreach (var method in this.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                try
                {
                    FunctionDefinition func = Indexer.GetDescriptionForMethod(method);
                    Assert.Fail("Expected error from method: {0}", method.Name);
                }
                catch (IndexException)
                {
                }
            }
        }

        private static void BadTableName([Table(@"#")] IAzureTableReader t) { }

        private static void MultipleQueueParams([QueueInput] int p123, [QueueInput] int p234) { }
    }
}
