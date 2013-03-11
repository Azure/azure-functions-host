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
            string configFile = @"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.local.cscfg";
            IAccountInfo account = LocalRunnerHost.Program.GetAccountInfo(configFile);
            var table = new AzureTable<TriggerReasonEntity>(account.GetAccount(), "functionCausalityLog");


            var log = new CausalityLogger(table, null);

            Guid g1 = Guid.NewGuid();
            Guid gParent = Guid.NewGuid();

            var reason = new BlobTriggerReason { BlobPath = new CloudBlobPath(@"abc\defg"), ChildGuid = g1, ParentGuid = gParent };
            log.LogTriggerReason(reason);

            var parent = log.GetChildren(gParent).ToArray();

        }

#if false
        Verify it still works locally (update host)

        ExecutionStatsAggregatorBridge on func complete. 
        - share with Executor?
        - realy should be part of IFunctionUpdatedLogger

        Get real orchestrator using it!

        - When do we delete Work items?

        A) ATHost can pull workers.  (But this means its own queue. Fighting AT model)
        B) Use AT queuing mechanism. 
#endif



        static void TestTask()
        {
            // Get account that service operates with
            string configFile = @"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Cloud-Mike.cscfg";
            IAccountInfo account = LocalRunnerHost.Program.GetAccountInfo(configFile);
            var config = LocalRunnerHost.Program.GetConfigAsDictionary(configFile);

            var taskConfig = new TaskConfig
            {
                TenantUrl = config["AzureTaskTenantUrl"],
                AccountName = config["AzureTaskAccountName"],
                Key = config["AzureTaskKey"],
                PoolName = config["AzureTaskPoolName"]
            };

            //IFunctionUpdatedLogger logger = new NullLogger();
            IFunctionUpdatedLogger logger = new FunctionInvokeLogger { Account = account.GetAccount(), TableName = "daasfunctionlogs" };

            ICausalityLogger causalityLogger = null;
            var e = new TaskExecutor(account, logger, taskConfig, causalityLogger);

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
                TriggerReason = new BlobTriggerReason { }, // !!! "test for Azure Tasks",
                Args = new ParameterRuntimeBinding[]
                  {
                       new LiteralStringParameterRuntimeBinding { Value = "172" }
                  },
                ServiceUrl = account.WebDashboardUri
            };
            ExecutionInstanceLogEntity entry = e.Queue(request);

            e.WaitAndPrintOutput(entry);
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
