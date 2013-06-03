using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;

namespace OrchestratorUnitTests
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

        [Timer("01:00:00")]
        private static void QueueAndTimer([QueueInput] int p123) { }
        
    }
}