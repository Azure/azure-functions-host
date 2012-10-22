using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orchestrator;
using SimpleBatch;

namespace OrchestratorUnitTests
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    [TestClass]
    public class FlowUnitTests
    {
        // Helper to do the indexing.
        private static FunctionIndexEntity Get(string methodName)
        {
            MethodInfo m = typeof(FlowUnitTests).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(m);

            FunctionIndexEntity func = Indexer.GetDescriptionForMethod(m);
            return func;
        }

        [NoAutomaticTrigger]
        private static void NoAutoTrigger1([BlobInput(@"daas-test-input\{name}.csv")] TextReader inputs) { }

        [TestMethod]
        public void TestNoAutoTrigger1()
        {
            FunctionIndexEntity func = Get("NoAutoTrigger1");
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs);
        }

        public static void AutoTrigger1([BlobInput(@"daas-test-input\{name}.csv")] TextReader inputs) { }
                   
        [TestMethod]
        public void AutoTrigger1()        
        {
            FunctionIndexEntity func = Get("AutoTrigger1");
            Assert.AreEqual(true, func.Trigger.ListenOnBlobs);
        }


        [NoAutomaticTrigger]
        public static void NoAutoTrigger2(int x, int y) { }
                   
        [TestMethod]
        public void TestNoAutoTrigger2()
        {
            FunctionIndexEntity func = Get("NoAutoTrigger2");
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs);
        }

        // Nothing about this method that is indexable.
        // No function (no trigger)
        public static void NoIndex(int x, int y) { }
        
        [TestMethod]
        public void TestNoIndex()
        {
            FunctionIndexEntity func = Get("NoIndex");
            Assert.IsNull(func);
        }

        // Runtime type is irrelevant for table. 
        private static void Table([Table("TableName")] object reader) { }

        [TestMethod]
        public void TestTable()
        {
            FunctionIndexEntity func = Get("Table");

            var flows = func.Flow.Bindings;
            Assert.AreEqual(1, flows.Length);

            var t = (TableParameterStaticBinding ) flows[0];
            Assert.AreEqual("TableName", t.TableName);
            Assert.AreEqual("reader", t.Name);
        }

        // Queue inputs with implicit names.
        public static void QueueIn([QueueInput] int inputQueue) { }

        [TestMethod]
        public void TestQueueInput()
        {
            FunctionIndexEntity func = Get("QueueIn");

            var flows = func.Flow.Bindings;
            Assert.AreEqual(1, flows.Length);

            var t = (QueueParameterStaticBinding)flows[0];            
            Assert.AreEqual("inputqueue", t.QueueName); // queue name gets normalized. 
            Assert.AreEqual("inputQueue", t.Name); // parameter name does not.
            Assert.IsTrue(t.IsInput);
        }

        // Queue inputs with implicit names.
        public static void QueueOutput([QueueOutput] out int inputQueue)
        {
            inputQueue = 0;
        }                  

        [TestMethod]
        public void TestQueueOutput()
        {
            FunctionIndexEntity func = Get("QueueOutput");
                        
            var flows = func.Flow.Bindings;
            Assert.AreEqual(1, flows.Length);

            var t = (QueueParameterStaticBinding)flows[0];
            Assert.AreEqual("inputqueue", t.QueueName);
            Assert.AreEqual("inputQueue", t.Name); // parameter name does not.
            Assert.IsFalse(t.IsInput);
        }

        [SimpleBatch.Description("This is a description")]
        public static void DescriptionOnly(string stuff) { }

        [TestMethod]
        public void TestDescriptionOnly()
        {
            FunctionIndexEntity func = Get("DescriptionOnly");
            
            Assert.AreEqual(false, func.Trigger.GetTimerInterval().HasValue); // no timer
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs); // no blobs
            
            var flows = func.Flow.Bindings;
            Assert.AreEqual(1, flows.Length);

            // Assumes any unrecognized parameters are supplied by the user
            var t = (NameParameterStaticBinding)flows[0];
            Assert.AreEqual("stuff", t.KeyName);
            Assert.AreEqual(true, t.UserSupplied);            
        }

        // Has an unbound parameter, so this will require an explicit invoke.  
        // Trigger: NoListener, explicit
        public static void HasBlobAndUnboundParameter([BlobInput("container")] Stream input, int unbound) { }
        
        [TestMethod]
        public void TestHasBlobAndUnboundParameter()
        {
            FunctionIndexEntity func = Get("HasBlobAndUnboundParameter");

            Assert.AreEqual(false, func.Trigger.GetTimerInterval().HasValue); // no timer
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs); // no blobs

            var flows = func.Flow.Bindings;
            Assert.AreEqual(2, flows.Length);

            var t0 = (BlobParameterStaticBinding)flows[0];
            Assert.AreEqual("container", t0.Path.ContainerName);
                        
            var t1 = (NameParameterStaticBinding)flows[1];
            Assert.AreEqual("unbound", t1.KeyName);
            Assert.AreEqual(true, t1.UserSupplied);
        }

        // Both parameters are bound. 
        // Trigger: Automatic listener
        public static void HasBlobAndBoundParameter([BlobInput(@"container\{bound}")] Stream input, int bound) { }

        [TestMethod]
        public void TestHasBlobAndBoundParameter()
        {
            FunctionIndexEntity func = Get("HasBlobAndBoundParameter");

            Assert.AreEqual(false, func.Trigger.GetTimerInterval().HasValue); // no timer
            Assert.AreEqual(true, func.Trigger.ListenOnBlobs); // all parameters are bound

            var flows = func.Flow.Bindings;
            Assert.AreEqual(2, flows.Length);

            var t0 = (BlobParameterStaticBinding)flows[0];
            Assert.AreEqual("container", t0.Path.ContainerName);

            var t1 = (NameParameterStaticBinding)flows[1];
            Assert.AreEqual("bound", t1.KeyName);
            Assert.AreEqual(false, t1.UserSupplied); // inferred from t0
        }        
    }
}
