using System.Reflection;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.UnitTests
{
    // Test failure cases for indexing
    public class FlowUnitTestErrors
    {
        [Fact]
        public void TestFails()
        {
            foreach (var method in this.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                Assert.Throws<IndexException>(() => Indexer.GetFunctionDefinition(method));
            }
        }

        private static void BadTableName([Table(@"#")] IAzureTableReader t) { }

        private static void MultipleQueueParams([QueueInput] int p123, [QueueInput] int p234) { }
    }
}
