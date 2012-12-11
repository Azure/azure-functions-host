using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;

namespace OrchestratorUnitTests
{
    // Unit test the static parameter bindings. This primarily tests the indexer.
    [TestClass]
    public class CodeConfigFlowUnitTests
    {
        #region Support
        private static FunctionIndexEntity Get(Type type)
        {
            var accountString = CloudStorageAccount.DevelopmentStorageAccount.ToString(true);


            IndexerSettings x = new IndexerSettings();
            Indexer i = new Indexer(x);

            Func<MethodInfo, FunctionLocation> funcApplyLocation =
                method => new FunctionLocation
                {
                    MethodName = method.Name,
                    TypeName = method.DeclaringType.Name,
                    Blob = new CloudBlobDescriptor
                    {
                        AccountConnectionString = accountString,
                        BlobName ="blob",
                        ContainerName = "container"
                    }
                };
                      
            
            i.IndexType(funcApplyLocation, type);

            var funcs = x.ReadAll();
            return funcs[0];
        }

        class IndexerSettings : IFunctionTable
        {
            private List<FunctionIndexEntity> _funcs = new List<FunctionIndexEntity>();

            public void Add(FunctionIndexEntity func)
            {
                _funcs.Add(func);
            }

            public void Delete(FunctionIndexEntity func)
            {
                throw new NotImplementedException();
            }

            public FunctionIndexEntity[] ReadAll()
            {
                return _funcs.ToArray();
            }

            public FunctionIndexEntity Lookup(string functionId)
            {
                throw new NotImplementedException();
            }
        }

        #endregion // Support

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

            Assert.AreEqual("TestReg", func.Location.MethodName);
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

            Assert.AreEqual("TestReg", func.Location.MethodName);
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

            Assert.AreEqual("TestReg", func.Location.MethodName);
            Assert.AreEqual("xyz", func.Description);
        }

        class Type4
        {
            public static void TestReg(Stream input)
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

            Assert.AreEqual("TestReg", func.Location.MethodName);
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
                config.Register("TestReg").TriggerTimer(TimeSpan.FromMinutes(3));
            }
        }

        [TestMethod]
        public void TestTimer()
        {
            var func = Get(typeof(Type5));

            Assert.IsNotNull(func);

            Assert.AreEqual("TestReg", func.Location.MethodName);
            Assert.AreEqual(false, func.Trigger.ListenOnBlobs);
            Assert.AreEqual("00:03:00", func.Trigger.TimerInterval);
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
            catch (InvalidOperationException e)
            {
            }            
        }
    }
}