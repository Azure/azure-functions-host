using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.WindowsAzure.Storage;
using Moq;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Indexers
{
    // Test failure cases for indexing
    public class FunctionIndexerIntegrationErrorTests
    {
        [Fact]
        public void TestFails()
        {
            FunctionIndexerContext context = FunctionIndexerContext.CreateDefault(
                new FunctionIndexContext(null, null, CloudStorageAccount.DevelopmentStorageAccount, null), null);

            foreach (var method in this.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                IFunctionIndex stubIndex = new Mock<IFunctionIndex>().Object;
                FunctionIndexer indexer = new FunctionIndexer(context);
                Assert.Throws<FunctionIndexingException>(() => indexer.IndexMethod(method, stubIndex));
            }
        }

        private static void BadTableName([Table(@"#")] IDictionary<Tuple<string, string>, object> t) { }

        private static void MultipleQueueParams([QueueTrigger("p123")] int p123, [QueueTrigger("p234")] int p234) { }

        private static void QueueNestedIEnumerable([Queue("myoutputqueue")] ICollection<IEnumerable<Payload>> myoutputqueue) { }

        private static void QueueOutputIList([Queue("myoutputqueue")] out IList<Payload> myoutputqueue) { myoutputqueue = null; }

        private static void FuncQueueOutputObject([Queue("myoutputqueue")] out object myoutputqueue) { myoutputqueue = null; }

        private static void FuncQueueOutputIEnumerableOfObject([Queue("myoutputqueue")] out IEnumerable<object> myoutputqueue) { myoutputqueue = null; }

        private class Payload
        {
            public int Value { get; set; }
        }
    }
}
