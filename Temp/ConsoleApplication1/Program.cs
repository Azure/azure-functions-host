using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;
using RunnerHost;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Data.Services.Common;
using AzureTables;
using SimpleBatch.Client;
using IndexDriver;
using System.Reflection;

namespace ConsoleApplication1
{

    public enum Fruit
    {
        Apple,
        Pear,
        Banana,
    }
    public class Widget
    {
        public string Name { get; set; }
        public int Score { get; set; }
    }

    [DataServiceKey("PartitionKey", "RowKey")]
    public class WidgetEntity : TableServiceEntity
    {
        //public Fruit Fruit { get; set; }
        public int Value { get; set; }

        public DateTime QueueTime { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        //public TimeSpan Delta { get; set; }
        //public TimeSpan DeltaMissing { get; set; }

        // TimeSpan

        // public Widget Widget { get; set; }
    }

    class Program
    {
        static void Main()
        {
            //TestIndex();
            TestTask();
        }

#if false
        
        Use a real IFunctionUpdatedLogger
            - need to pass more config in. 


        Get real orchestrator using it!

        - When do we delete Work items?

        A) ATHost can pull workers.  (But this means its own queue. Fighting AT model)
        B) Use AT queuing mechanism. 
#endif

        static void TestTask()
        {
            // Get account that service operates with
            IAccountInfo account = LocalRunnerHost.Program.GetAccountInfo(@"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Cloud-Mike.cscfg");
            
            // !!! Move to config file or something
            var taskConfig = new TaskConfig
            {
                TenantUrl = "https://task.core.windows-int.net",
                AccountName = "simplebatch1",
                Key = "???",
                PoolName = "SimpleBatchPool123"
            };
        

            var e = new TaskExecutor(account, new NullLogger(), taskConfig);

            //e.DeletePool();
            //e.CreatePool(1);



            string userAccountConnectionString = account.AccountConnectionString; // account for user functions

            FunctionInvokeRequest request = new FunctionInvokeRequest
            {
                Location = new FunctionLocation
                {
                    Blob = new CloudBlobDescriptor
                        {
                            AccountConnectionString = userAccountConnectionString,
                            ContainerName = "daas-test-functions",
                            BlobName = "TestApp1.exe"
                        },
                    TypeName = "TestApp1.Program",
                    MethodName = "TestCall2"
                },
                TriggerReason = "test for Azure Tasks",
                Args = new ParameterRuntimeBinding[]
                  {
                       new LiteralStringParameterRuntimeBinding { Value = "172" }
                  },
                ServiceUrl = account.WebDashboardUri
            };
            e.Queue(request);


            // Wait for execution
            while (true)
            {
                Thread.Sleep(1000);
            }

            
        }

        

        class NullLogger : IFunctionUpdatedLogger
        {
            public void Log(ExecutionInstanceLogEntity func)
            {                
            }
        }

        static void TestIndex()
        {
            IFunctionTable x = new FuncTable();
            Indexer i = new Indexer(x);


            Func<MethodInfo, FunctionLocation> funcApplyLocation = method => null;
                    

            string dir = @"C:\CodePlex\azuresimplebatch\Temp\TestApp1\bin\Debug";
            i.IndexLocalDir(funcApplyLocation, dir);
        }

        class FuncTable : IFunctionTable
        {
            public void Add(FunctionIndexEntity func)
            {
            }

            public void Delete(FunctionIndexEntity func)
            {
            }

            public FunctionIndexEntity Lookup(string functionId)
            {
                throw new NotImplementedException();
            }

            public FunctionIndexEntity[] ReadAll()
            {
                return new FunctionIndexEntity[0];
            }
        }
    }
}
