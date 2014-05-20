using System;
using AzureTables;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Runners;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Ensure that various "currency" types can be properly serialized and deserialized to AzureTables.
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

        [Fact]
        public void TestExecutionInstanceLogEntity()
        {
            Guid g = Guid.Parse("508B5DE1-C1B8-431C-81E6-DDC7D7E195DC");

            var instance = new FunctionInvokeRequest
            {
                Id = g,
                TriggerReason = new BlobTriggerReason { },
                Location = new MethodInfoFunctionLocation
                {
                    StorageConnectionString = "some connection string",
                    MethodName = "method"
                },
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
                ParameterLogUrl = "http://output2",
                StartTime = now
            };


            var table =  GetTable<ExecutionInstanceLogEntity>();
            ExecutionInstanceLogEntitySerializer serializer = new ExecutionInstanceLogEntitySerializer(table);

            serializer.Log(log);
            // $$$ Get a new instance of the table to ensure everything was flushed. 

            var log2 = serializer.Read(g);

            Assert.NotNull(log2);
            Assert.False(Object.ReferenceEquals(log, log2), "looked up object should be new instance");

            AssertEqual(log.FunctionInstance, log2.FunctionInstance);

            Assert.Equal(log.StartTime, log2.StartTime);
            Assert.Equal(log.EndTime, log2.EndTime);

            Assert.Equal(log.ToString(), log2.ToString());
        }

        void AssertEqual(FunctionInvokeRequest instance1, FunctionInvokeRequest instance2)
        {
            Assert.Equal(instance1.Id, instance2.Id);
            Assert.Equal(instance1.Args.Length, instance2.Args.Length);

            for (int i = 0; i < instance1.Args.Length; i++)
            {
                var arg1 = instance1.Args[i];
                var arg2 = instance2.Args[i];

                Assert.Equal(GetInvokeString(arg1), GetInvokeString(arg2));
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

        [Fact]
        public void TestSerializeFunctionIndexEntity()
        {
            // Create an elaborate function object and ensure we can write it to table storage.

            FunctionDefinition func = new FunctionDefinition
            {
                Location = new MethodInfoFunctionLocation
                {
                    Id = "some Id",
                    MethodName = "method",
                },
                Trigger = new FunctionTrigger
                {
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
                            Name = "key"
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
            Assert.NotNull(func2);

            Assert.Equal(func.Location, func2.Location);

            Assert.Equal(func.Trigger.ListenOnBlobs, func2.Trigger.ListenOnBlobs);

            // Verify bindings. 
            Assert.Equal(func.Flow.Bindings.Length, func2.Flow.Bindings.Length);

            for (int i = 0; i < func.Flow.Bindings.Length; i++)
            {
                var f1 = func.Flow.Bindings[i];
                var f2 = func2.Flow.Bindings[i];

                Assert.Equal(f1.ToString(), f2.ToString());
            }
        }
    }
}
