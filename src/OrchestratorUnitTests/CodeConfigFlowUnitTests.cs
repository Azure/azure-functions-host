using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure;




namespace Microsoft.WindowsAzure.JobsUnitTests
{
    /* TODO: Reinstate fluent configuration
    // Unit test the static parameter bindings. This primarily tests the indexer.
    [TestClass]
    public class CodeConfigFlowUnitTests
    {
        // Get the SimpleBatch function that's declared in the given type. Assumes only 1 function per type.
        private static FunctionDefinition Get(Type type)
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            var x = new LocalFunctionTable(account);
            IFunctionTable functionTable = x;
            Indexer i = new Indexer(functionTable);
           
            i.IndexType(x.OnApplyLocationInfo, type);

            var funcs = functionTable.ReadAll();
            return funcs[0];
        }

        class Type1
        {
            public static void TestReg(TextReader input, TextWriter output)
            {
                string content = input.ReadToEnd();
                output.Write(input);
            }

            public static void Initialize(IConfiguration config)
            {
                config.Register("TestReg").
                    BindBlobInput("input", @"daas-test-input3\{name}.csv").
                    BindBlobOutput("output", @"daas-test-input3\{name}.output.csv");
            }
        }

        [TestMethod]
        public void Test1()
        {
            var func = Get(typeof(Type1));

            Assert.AreEqual("Type1.TestReg", func.Location.GetShortName());
            Assert.AreEqual(true, func.Trigger.ListenOnBlobs);

            var bindings = func.Flow.Bindings;
            Assert.AreEqual(2, bindings.Length);

            Assert.AreEqual("input", bindings[0].Name);
            Assert.AreEqual(bindings[0].Description, @"Read from blob: daas-test-input3\{name}.csv");

            Assert.AreEqual("output", bindings[1].Name);
            Assert.AreEqual(bindings[1].Description, @"Write to blob: daas-test-input3\{name}.output.csv");
        }

        class Type2
        {
            // No declarations
            public static void TestReg()
            {
            }

            public static void Initialize(IConfiguration config)
            {
                config.Register("TestReg");
            }
        }

        [TestMethod]
        public void TestNoDecls()
        {
            var func = Get(typeof(Type2));

            Assert.IsNotNull(func);

            Assert.AreEqual("Type2.TestReg", func.Location.GetShortName());
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs);

            var bindings = func.Flow.Bindings;
            Assert.AreEqual(0, bindings.Length);
        }

        class Type3
        {
            public static void TestReg()
            {
            }

            public static void Initialize(IConfiguration config)
            {
                config.Register("TestReg").Description("xyz");
            }
        }

        [TestMethod]
        public void TestDescription()
        {
            var func = Get(typeof(Type3));

            Assert.IsNotNull(func);

            Assert.AreEqual("Type3.TestReg", func.Location.GetShortName());
            Assert.AreEqual("xyz", func.Description);
        }

        class Type4
        {
            public static void TestReg()
            {
            }

            public static void Initialize(IConfiguration config)
            {
                config.Register("TestReg").TriggerNoAutomatic();
            }
        }

        [TestMethod]
        public void TestNoAutoTrigger()
        {
            var func = Get(typeof(Type4));

            Assert.IsNotNull(func);

            Assert.AreEqual("Type4.TestReg", func.Location.GetShortName());
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs);
            Assert.AreEqual(null, func.Trigger.TimerInterval);
        }


        class Type5
        {
            public static void TestReg(Stream input)
            {
            }

            public static void Initialize(IConfiguration config)
            {
                config.Register("TestReg").TriggerTimer(TimeSpan.FromMinutes(3)).BindBlobInput("input", "container/blob");
            }
        }

        [TestMethod]
        public void TestTimer()
        {
            var func = Get(typeof(Type5));

            Assert.IsNotNull(func);

            Assert.AreEqual("Type5.TestReg", func.Location.GetShortName());
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs);
            Assert.AreEqual(TimeSpan.Parse("00:03:00"), func.Trigger.TimerInterval);
        }

        class Type6
        {
            public static void TestReg(Stream input)
            {
            }

            public static void Initialize(IConfiguration config)
            {
                // Try to register with a bad parameter name. 
                // How do these errors flow? Thrown immediately? Thrown after we return?
                // either way, must be debuggable.
                config.Register("TestReg").Bind("missing", new BlobInputAttribute(@"container\blob"));
            }
        }

        [TestMethod]
        public void TestBadParameterName()
        {
            try
            {
                var func = Get(typeof(Type6));
                Assert.Fail("shouldn't succeed at registering missing parameter name.");
            }
            catch (InvalidOperationException)
            {
            }            
        }
    }
     * */
}