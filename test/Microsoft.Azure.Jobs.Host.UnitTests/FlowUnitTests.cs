using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    public class FlowUnitTests
    {
        // Helper to do the indexing.
        private static FunctionDefinition Get(string methodName, INameResolver nameResolver = null)
        {
            MethodInfo m = typeof(FlowUnitTests).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(m);

            Indexer idx = new Indexer(null, nameResolver);
            FunctionDefinition func = idx.GetFunctionDefinition(m, null);
            return func;
        }

        [NoAutomaticTrigger]
        private static void NoAutoTrigger1([BlobInput(@"daas-test-input/{name}.csv")] TextReader inputs) { }

        [Fact]
        public void TestNoAutoTrigger1()
        {
            FunctionDefinition func = Get("NoAutoTrigger1");
            Assert.Equal(false, func.Trigger.ListenOnBlobs);
        }

        [NoAutomaticTrigger]
        private static void NameResolver([BlobInput(@"input/%name%")] TextReader inputs) { }

        [Fact]
        public void TestNameResolver()
        {
            DictNameResolver nameResolver = new DictNameResolver();
            nameResolver.Add("name", "VALUE");
            FunctionDefinition func = Get("NameResolver", nameResolver);
            var bindings = func.Flow.Bindings;

            Assert.Equal(@"input/VALUE", ((BlobParameterStaticBinding)bindings[0]).Path.ToString());
        }

        public static void AutoTrigger1([BlobInput(@"daas-test-input/{name}.csv")] TextReader inputs) { }
                   
        [Fact]
        public void TestAutoTrigger1()        
        {
            FunctionDefinition func = Get("AutoTrigger1");
            Assert.Equal(true, func.Trigger.ListenOnBlobs);
        }

        [NoAutomaticTrigger]
        public static void NoAutoTrigger2(int x, int y) { }
                   
        [Fact]
        public void TestNoAutoTrigger2()
        {
            FunctionDefinition func = Get("NoAutoTrigger2");
            Assert.Equal(false, func.Trigger.ListenOnBlobs);
        }

        // Nothing about this method that is indexable.
        // No function (no trigger)
        public static void NoIndex(int x, int y) { }
        
        [Fact]
        public void TestNoIndex()
        {
            FunctionDefinition func = Get("NoIndex");
            Assert.Null(func);
        }

        // Runtime type is irrelevant for table. 
        private static void Table([Table("TableName")] object reader) { }

        [Fact]
        public void TestTable()
        {
            FunctionDefinition func = Get("Table");

            var flows = func.Flow.Bindings;
            Assert.Equal(1, flows.Length);

            var t = (TableParameterStaticBinding ) flows[0];
            Assert.Equal("TableName", t.TableName);
            Assert.Equal("reader", t.Name);
        }

        public static void QueueTrigger([QueueTrigger("inputQueue")] int queueValue) { }

        [Fact]
        public void TestQueueTrigger()
        {
            FunctionDefinition func = Get("QueueTrigger");

            Assert.NotNull(func);

            QueueTriggerBinding binding = func.TriggerBinding as QueueTriggerBinding;

            Assert.NotNull(binding);
            Assert.Equal("inputqueue", binding.QueueName); // queue name gets normalized. 
            Assert.Equal("queueValue", func.TriggerParameterName); // parameter name does not.
        }

        // Queue inputs with implicit names.
        public static void QueueOutput([QueueOutput] out int inputQueue)
        {
            inputQueue = 0;
        }                  

        [Fact]
        public void TestQueueOutput()
        {
            FunctionDefinition func = Get("QueueOutput");
                        
            var flows = func.Flow.Bindings;
            Assert.Equal(1, flows.Length);

            var t = (QueueParameterStaticBinding)flows[0];
            Assert.Equal("inputqueue", t.QueueName);
            Assert.Equal("inputQueue", t.Name); // parameter name does not.
        }

        [Jobs.Description("This is a description")]
        public static void DescriptionOnly(string stuff) { }

        [Fact]
        public void TestDescriptionOnly()
        {
            FunctionDefinition func = Get("DescriptionOnly");
            
            Assert.Equal(false, func.Trigger.ListenOnBlobs); // no blobs
            
            var flows = func.Flow.Bindings;
            Assert.Equal(1, flows.Length);

            // Assumes any unrecognized parameters are supplied by the user
            var t = (InvokeParameterStaticBinding)flows[0];
            Assert.Equal("stuff", t.Name);
        }

        // Has an unbound parameter, so this will require an explicit invoke.  
        // Trigger: NoListener, explicit
        public static void HasBlobAndUnboundParameter([BlobInput("container")] Stream input, int unbound) { }
        
        [Fact]
        public void TestHasBlobAndUnboundParameter()
        {
            FunctionDefinition func = Get("HasBlobAndUnboundParameter");

            Assert.Equal(true, func.Trigger.ListenOnBlobs); // no blobs

            var flows = func.Flow.Bindings;
            Assert.Equal(2, flows.Length);

            var t0 = (BlobParameterStaticBinding)flows[0];
            Assert.Equal("container", t0.Path.ContainerName);
                        
            var t1 = (InvokeParameterStaticBinding)flows[1];
            Assert.Equal("unbound", t1.Name);
        }

        // Both parameters are bound. 
        // Trigger: Automatic listener
        public static void HasBlobAndBoundParameter([BlobInput(@"container/{bound}")] Stream input, int bound) { }

        [Fact]
        public void TestHasBlobAndBoundParameter()
        {
            FunctionDefinition func = Get("HasBlobAndBoundParameter");

            Assert.Equal(true, func.Trigger.ListenOnBlobs); // all parameters are bound

            var flows = func.Flow.Bindings;
            Assert.Equal(2, flows.Length);

            var t0 = (BlobParameterStaticBinding)flows[0];
            Assert.Equal("container", t0.Path.ContainerName);

            var t1 = (NameParameterStaticBinding)flows[1];
            Assert.Equal("bound", t1.Name);
        }        
    }
}
