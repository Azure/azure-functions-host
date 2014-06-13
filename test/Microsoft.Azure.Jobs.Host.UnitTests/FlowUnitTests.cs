using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.Data;
using Microsoft.Azure.Jobs.Host.Bindings.Invoke;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Queues.Bindings;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.Azure.Jobs.Host.Tables;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
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

            Indexer idx = new Indexer(null, nameResolver, null, CloudStorageAccount.DevelopmentStorageAccount, null);
            FunctionDefinition func = idx.CreateFunctionDefinition(m);
            return func;
        }

        private static void NoAutoTrigger1([Blob(@"daas-test-input/{name}.csv")] TextReader inputs) { }

        [Fact]
        public void TestNoAutoTrigger1()
        {
            FunctionDefinition func = Get("NoAutoTrigger1");
            Assert.Null(func.TriggerBinding);
        }

        private static void NameResolver([Blob(@"input/%name%")] TextReader inputs) { }

        [Fact]
        public void TestNameResolver()
        {
            DictNameResolver nameResolver = new DictNameResolver();
            nameResolver.Add("name", "VALUE");
            FunctionDefinition func = Get("NameResolver", nameResolver);
            var bindings = func.NonTriggerBindings;

            Assert.Equal(@"input/VALUE", ((BlobBinding)bindings["inputs"]).BlobPath);
        }

        public static void AutoTrigger1([BlobTrigger(@"daas-test-input/{name}.csv")] TextReader inputs) { }

        [Fact]
        public void TestAutoTrigger1()
        {
            FunctionDefinition func = Get("AutoTrigger1");
            Assert.IsType<BlobTriggerBinding>(func.TriggerBinding);
        }

        [NoAutomaticTrigger]
        public static void NoAutoTrigger2(int x, int y) { }

        [Fact]
        public void TestNoAutoTrigger2()
        {
            FunctionDefinition func = Get("NoAutoTrigger2");
            Assert.Null(func.TriggerBinding);
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

        private static void Table([Table("TableName")] CloudTable table) { }

        [Fact]
        public void TestTable()
        {
            FunctionDefinition func = Get("Table");

            var flows = func.NonTriggerBindings;
            Assert.Equal(1, flows.Count);

            Assert.True(flows.ContainsKey("table"));
            var t = (TableBinding)flows["table"];
            Assert.Equal("TableName", t.TableName);
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
        public static void QueueOutput([Queue("inputQueue")] out int inputQueue)
        {
            inputQueue = 0;
        }

        [Fact]
        public void TestQueueOutput()
        {
            FunctionDefinition func = Get("QueueOutput");

            var bindings = func.NonTriggerBindings;
            Assert.Equal(1, bindings.Count);

            Assert.True(bindings.ContainsKey("inputQueue")); // parameter name does not.
            var t = (QueueBinding)bindings["inputQueue"];
            Assert.Equal("inputqueue", t.QueueName);
        }

        // Has an unbound parameter, so this will require an explicit invoke.  
        // Trigger: NoListener, explicit
        [NoAutomaticTrigger]
        public static void HasBlobAndUnboundParameter([BlobTrigger("container")] Stream input, int unbound) { }

        [Fact]
        public void TestHasBlobAndUnboundParameter()
        {

            FunctionDefinition func = Get("HasBlobAndUnboundParameter");

            Assert.IsType<BlobTriggerBinding>(func.TriggerBinding); // no blobs
            Assert.Equal("container", ((BlobTriggerBinding)func.TriggerBinding).ContainerName);

            var flows = func.NonTriggerBindings;
            Assert.Equal(1, flows.Count);

            Assert.True(flows.ContainsKey("unbound"));
            Assert.IsType<StructInvokeBinding<int>>(flows["unbound"]);
        }

        // Both parameters are bound. 
        // Trigger: Automatic listener
        public static void HasBlobAndBoundParameter([BlobTrigger(@"container/{bound}")] Stream input, int bound) { }

        [Fact]
        public void TestHasBlobAndBoundParameter()
        {
            FunctionDefinition func = Get("HasBlobAndBoundParameter");

            Assert.IsType<BlobTriggerBinding>(func.TriggerBinding); // all parameters are bound
            Assert.Equal("container", ((BlobTriggerBinding)func.TriggerBinding).ContainerName);

            var flows = func.NonTriggerBindings;
            Assert.Equal(1, flows.Count);

            Assert.True(flows.ContainsKey("bound"));
            Assert.IsType<ClassDataBinding<string>>(flows["bound"]);
        }
    }
}
