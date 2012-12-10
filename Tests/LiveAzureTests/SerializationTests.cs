using System;
using AzureTables;
using Executor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orchestrator;
using RunnerHost;
using RunnerInterfaces;

namespace LiveAzureTests
{
    [TestClass]
    public class SerializationTests
    {
        static CloudBlobDescriptor Blob(string container, string blob)
        {
            return new CloudBlobDescriptor
            {
                AccountConnectionString = AzureConfig.GetConnectionString(),
                ContainerName = container,
                BlobName = blob,
            };
        }

        [TestMethod]
        public void TestExecutionInstanceLogEntity()
        {
            Guid g = Guid.Parse("508B5DE1-C1B8-431C-81E6-DDC7D7E195DC");

            var instance = new FunctionInvokeRequest
            {
                Id =  g, 
                TriggerReason = "from test",
                Location = new FunctionLocation
                {
                    Blob = Blob("container", "blob"),
                    MethodName = "method",
                    TypeName = "type"
                },
                ParameterLogBlob = Blob("logs", "param"),
                SchemaNumber = 4,
                Args = new ParameterRuntimeBinding[]
                {
                    new BlobParameterRuntimeBinding
                    { 
                        Blob = Blob("container", "blob")
                    },
                    new TableParameterRuntimeBinding
                    {
                         Table = new CloudTableDescriptor
                         {
                              AccountConnectionString = AzureConfig.GetConnectionString(),
                              TableName  = "mytable7"
                         }                         
                    },
                    new LiteralStringParameterRuntimeBinding  
                    {
                          Value = "abc"
                    },
                    new QueueOutputParameterRuntimeBinding 
                    {
                         QueueOutput = new CloudQueueDescriptor
                         {
                              AccountConnectionString = AzureConfig.GetConnectionString(),
                              QueueName = "myqueue"
                         }
                    }
                }
            };

            var now = DateTime.UtcNow;
            var log = new ExecutionInstanceLogEntity
            {
                FunctionInstance = instance,
                 ExceptionType = "system.CrazyException",
                 ExceptionMessage = "testing",
                 OutputUrl = "http://output",
                 QueueTime = now.Subtract(TimeSpan.FromMinutes(5)),                     
                 StartTime = now
            };


            string tableName = "functionlogtest";
            Utility.DeleteTable(AzureConfig.GetAccount(), tableName);

            IFunctionUpdatedLogger logger = new FunctionInvokeLogger { _account = AzureConfig.GetAccount(), _tableName = tableName };

            logger.Log(log);


            var lookupTable = new AzureTable<ExecutionInstanceLogEntity>(
                 AzureConfig.GetAccount(), tableName);                 
            IFunctionInstanceLookup lookup = new ExecutionStatsAggregator(lookupTable);

            var log2 = lookup.Lookup(g);

            Assert.IsNotNull(log2);

            AssertEqual(log.FunctionInstance, log2.FunctionInstance);

            AssertEqual(log.QueueTime, log2.QueueTime);
            AssertEqual(log.StartTime, log2.StartTime);
            AssertEqual(log.EndTime, log2.EndTime);

            Assert.AreEqual(log.ToString(), log2.ToString());
        }

        void AssertEqual(DateTime? a, DateTime? b)
        {
            if (a.HasValue != b.HasValue)
            {
                Assert.AreEqual(a, b); // will fail
            }
            if (!a.HasValue)
            {
                Assert.IsNull(b);
                return;
            }
            Assert.AreEqual(a.Value.ToUniversalTime(), b.Value.ToUniversalTime());
        }

        void AssertEqual(FunctionInvokeRequest instance1, FunctionInvokeRequest instance2)
        {
            Assert.AreEqual(instance1.Id, instance2.Id);
            Assert.AreEqual(instance1.Args.Length, instance2.Args.Length);

            for (int i = 0; i < instance1.Args.Length; i++)
            {
                var arg1 = instance1.Args[i];
                var arg2 = instance2.Args[i];

                Assert.AreEqual(GetInvokeString(arg1), GetInvokeString(arg2));
            }            
        }

        static string GetInvokeString(ParameterRuntimeBinding p)
        {
            try
            {
                return p.ConvertToInvokeString();
            }
            catch (NotImplementedException)
            {
                return null;
            }
        }

        [TestMethod]
        public void TestSerializeFunctionIndexEntity()
        {
            // Create an elaborate function object and ensure we can write it to table storage.

            FunctionIndexEntity func = new FunctionIndexEntity
            {
                Description = "description",
                Location = new FunctionLocation
                {
                    Blob = Blob("container", "blob"),
                    MethodName = "method",
                    TypeName = "type"
                },
                Trigger = new FunctionTrigger
                {
                    TimerInterval = TimeSpan.FromMinutes(15).ToString(),
                    ListenOnBlobs = true
                },
                Flow = new FunctionFlow
                {
                    // especially useful to hit different binding types. 
                    Bindings = new ParameterStaticBinding[]
                    {
                        new BlobParameterStaticBinding { 
                            IsInput = true,
                            Path = new CloudBlobPath(Blob("container", "blob"))
                        },
                        new QueueParameterStaticBinding
                        {
                            IsInput = true,
                            Name = "myQueue", // casing
                            QueueName = "myqueue"
                        },
                        new TableParameterStaticBinding 
                        {
                            TableName = "myTable7"
                        },
                        new NameParameterStaticBinding
                        {
                            KeyName = "key"
                        }
                    }
                }
            };
            func.SetRowKey();

            string tableName = "functabletest";
            Utility.AddTableRow(AzureConfig.GetAccount(), tableName, func);

            var func2 = Utility.Lookup<FunctionIndexEntity>(AzureConfig.GetAccount(), tableName, func.PartitionKey, func.RowKey);
            

            // Ensure it round tripped.
            Assert.IsNotNull(func2, "failed to lookup");
            Assert.AreEqual(func.Description, func2.Description); // should be easy

            Assert.AreEqual(func.Location, func2.Location);

            Assert.AreEqual(func.Trigger.TimerInterval, func2.Trigger.TimerInterval);
            Assert.AreEqual(func.Trigger.ListenOnBlobs, func2.Trigger.ListenOnBlobs);

            // Verify bindings. 
            Assert.AreEqual(func.Flow.Bindings.Length, func2.Flow.Bindings.Length);

            for (int i = 0; i < func.Flow.Bindings.Length; i++)
            {
                var f1 = func.Flow.Bindings[i];
                var f2 = func2.Flow.Bindings[i];

                Assert.AreEqual(f1.ToString(), f2.ToString());
            }
        }
    }
}
