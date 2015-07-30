// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Indexers
{
    // Test failure cases for indexing
    public class FunctionIndexerIntegrationErrorTests
    {
        [Fact]
        public void TestFails()
        {
            foreach (var method in this.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                Mock<IExtensionRegistry> extensionsMock = new Mock<IExtensionRegistry>(MockBehavior.Strict);
                extensionsMock.Setup(p => p.GetExtensions(typeof(IExtensionConfigProvider))).Returns(Enumerable.Empty<object>());
                extensionsMock.Setup(p => p.GetExtensions(typeof(ITriggerBindingProvider))).Returns(Enumerable.Empty<object>());
                extensionsMock.Setup(p => p.GetExtensions(typeof(IBindingProvider))).Returns(Enumerable.Empty<object>());
                extensionsMock.Setup(p => p.GetExtensions(typeof(IArgumentBindingProvider<>))).Returns(Enumerable.Empty<object>());
                Mock<IFunctionExecutor> executorMock = new Mock<IFunctionExecutor>(MockBehavior.Strict);

                IFunctionIndexCollector stubIndex = new Mock<IFunctionIndexCollector>().Object;
                FunctionIndexer indexer = new FunctionIndexer(
                    new Mock<ITriggerBindingProvider>(MockBehavior.Strict).Object,
                    new Mock<IBindingProvider>(MockBehavior.Strict).Object,
                    new Mock<IJobActivator>(MockBehavior.Strict).Object,
                    executorMock.Object,
                    extensionsMock.Object,
                    new SingletonManager());

                Assert.Throws<FunctionIndexingException>(() => indexer.IndexMethodAsync(method, stubIndex, CancellationToken.None).GetAwaiter().GetResult());
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
