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
using DaasEndpoints;
using Microsoft.Win32;
using System.Net;
using System.Runtime.InteropServices;

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
        // http://stackoverflow.com/questions/105031/how-do-you-get-total-amount-of-ram-the-computer-has
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private static float GetCpuClockSpeed()
        {
            return (int)Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0);
        }

        static void Record()
        {
            ulong installedMemory;
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                installedMemory = memStatus.ullTotalPhys;

                float x = installedMemory;
            }


            ExecutionNodeTrackingStats h = new ExecutionNodeTrackingStats
            {
                 ClockSpeed = GetCpuClockSpeed(),
                 NumCores = Environment.ProcessorCount,
                 OSVersion = Environment.OSVersion.ToString(),
                 AccountName = "test2"
            };

            var url = @"http://simplebatch.azurewebsites.net/api/UsageStats";
            PostJson(url, h);
        }

        static void PostJson(string url, object body)
        {
            var json = JsonConvert.SerializeObject(body);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            WebRequest request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length; // set before writing to stream
            var stream = request.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Close();

            var response = request.GetResponse(); // does the actual web request
        }

        public class ExecutionNodeTrackingStats
        {
            public int NumCores { get; set; }
            public float ClockSpeed { get; set; }
            public string OSVersion { get; set; }
            public string AccountName { get; set; } 
        }


        static void Main()
        {
            Record();
            return;

            string configFile = @"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Cloud-Daas.cscfg";
            IAccountInfo accountInfo = LocalRunnerHost.Program.GetAccountInfo(configFile);

            Services services = new Services(accountInfo);
            IFunctionInstanceQuery logger = services.GetFunctionInvokeQuery();

            var la = new WebFrontEnd.LogAnalysis();
            var rows = la.GetChargebackLog(30, "", logger);
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
                TriggerReason = new BlobTriggerReason { }, 
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
