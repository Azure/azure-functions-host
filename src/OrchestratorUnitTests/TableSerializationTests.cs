using System;
using AzureTables;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;

namespace Microsoft.WindowsAzure.JobsUnitTests
{
    // Ensure that various "currency" types can be properly serialized and deserialized to AzureTables.
    [TestClass]
    public class TableSerializationTests
    {
        internal AzureTable<T> GetTable<T>() where T : new()
        {
            return AzureTable<T>.NewInMemory();
        }

        static CloudBlobDescriptor Blob(string container, string blob)
        {
            return new CloudBlobDescriptor
            {
                AccountConnectionString = AccountConnectionString,
                ContainerName = container,
                BlobName = blob,
            };
        }
        const string AccountConnectionString = "name=some azure account;password=secret";

        [TestMethod]
        public void TestExecutionInstanceLogEntity()
        {
            Guid g = Guid.Parse("508B5DE1-C1B8-431C-81E6-DDC7D7E195DC");

            var instance = new FunctionInvokeRequest
            {
                Id = g,
                TriggerReason = new BlobTriggerReason { },
                Location = new RemoteFunctionLocation
                {
                    AccountConnectionString = "some connection string",
                    DownloadSource = new CloudBlobPath("container", "blob"),
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
                              AccountConnectionString = AccountConnectionString,
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
                              AccountConnectionString = AccountConnectionString,
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


            var table =  GetTable<ExecutionInstanceLogEntity>();
            IFunctionUpdatedLogger logger = new FunctionUpdatedLogger(table);

            logger.Log(log);
            // $$$ Get a new instance of the table to ensure everything was flushed. 

            IFunctionInstanceLookup lookup = new ExecutionStatsAggregator(table);

            var log2 = lookup.Lookup(g);

            Assert.IsNotNull(log2);
            Assert.IsFalse(Object.ReferenceEquals(log, log2), "looked up object should be new instance");

            AssertEqual(log.FunctionInstance, log2.FunctionInstance);

            Assert.AreEqual(log.QueueTime, log2.QueueTime);
            Assert.AreEqual(log.StartTime, log2.StartTime);
            Assert.AreEqual(log.EndTime, log2.EndTime);

            Assert.AreEqual(log.ToString(), log2.ToString());
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

            FunctionDefinition func = new FunctionDefinition
            {
                Description = "description",
                Location = new LocalFunctionLocation
                {
                    AssemblyPath = @"unknown:\unknown",
                    MethodName = "method",
                    TypeName = "type"
                },
                Trigger = new FunctionTrigger
                {
                    TimerInterval = TimeSpan.FromMinutes(15),
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

            IAzureTable<FunctionDefinition> table = GetTable<FunctionDefinition>();

            string partKey = "1";
            string rowKey = func.ToString();
            table.Write(partKey, rowKey, func);
            table.Flush();

            FunctionDefinition func2 = table.Lookup(partKey, rowKey);

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