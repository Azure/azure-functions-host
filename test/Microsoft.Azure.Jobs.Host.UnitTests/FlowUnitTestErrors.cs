using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Indexers;
using Moq;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Test failure cases for indexing
    public class FlowUnitTestErrors
    {
        [Fact]
        public void TestFails()
        {
            FunctionIndexerContext context = FunctionIndexerContext.CreateDefault(
                new FunctionIndexContext(null, null, null, null), null);

            foreach (var method in this.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                IFunctionIndex stubIndex = new Mock<IFunctionIndex>().Object;
                FunctionIndexer indexer = new FunctionIndexer(context);
                Assert.Throws<FunctionIndexingException>(() => indexer.IndexMethod(method, stubIndex));
            }
        }

        private static void BadTableName([Table(@"#")] IDictionary<Tuple<string, string>, object> t) { }

        private static void MultipleQueueParams([QueueTrigger("p123")] int p123, [QueueTrigger("p234")] int p234) { }
    }
}
