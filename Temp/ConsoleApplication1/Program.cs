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
            Invoke();
            //Watch();
            //Stats();
            //Log();
            //Table();
            //TimerFunc();
            //Execute();
            //Indexing();
            //Listener();
            //Orch();
            //Run();

            //Upload();
            //Dict();
            // Dustin();
            //Walk();
        }

        static void Invoke()
        {
            // Invoke: mike\daas-test-functions\TestApp1.exe\TestApp1.Program\T
            var c = new WebFunctionInvoker(
                @"mike\daas-test-functions\TestApp1.exe\TestApp1.Program\",
                @"http://daas2.azurewebsites.net/"
                //@"http://localhost:44498/"
                );

            c.Invoke("T"); // blocks

        }

        private static void Watch()
        {
            var x = new WatchableEnumerable<int>(new int[] { 1, 2, 3, 4, 5 });
            foreach (var i in x)
            {
                string s = x.GetStatus();
            }
        }

        private static void Stats()
        {
            var table = new AzureTable(Secrets.GetAccount(), "FunctionInvokeStats");
            var logger = new FunctionInvokeLogger { _account = Secrets.GetAccount(), _tableName = Secrets.FunctionInvokeLogTableName };
            var s = new ExecutionStatsAggregator(table, logger, null);

            Stopwatch sw = Stopwatch.StartNew();
            int count = 0;
            var all = Utility.ReadTableLazy<ExecutionInstanceLogEntity>(Secrets.GetAccount(), Secrets.FunctionInvokeLogTableName);
            foreach (var log in all)
            {
                s.OnFunctionComplete(log.FunctionInstance.Id);
                count++;

                if (count % 500 == 0)
                {
                    Console.WriteLine("time: {0}, count {1}", sw.Elapsed, count);
                    s.Flush();
                }
            }
            sw.Stop();            
            s.Flush();
            Console.WriteLine("Total time: {0}", sw.Elapsed);
        }

        private static void Table()
        {
            //var b = Utility.IsSimpleType(typeof(DateTime?));

            WidgetEntity w = new WidgetEntity
            {
                PartitionKey = "1",
                RowKey = "2",
                //Fruit = Fruit.Banana,
                Value = 15,
                StartTime = null,
                EndTime = DateTime.Now,
                //Delta = TimeSpan.FromMinutes(17)
            };

            string tableName = "test19b";
            Utility.AddTableRow(Secrets.GetAccount(), tableName, w);

            var w2 = Utility.Lookup<WidgetEntity>(Secrets.GetAccount(), tableName, "1", "2");
        }


        private static void Log()
        {
            var a  = Secrets.GetAccount();
            var client = a.CreateCloudBlobClient();
            var c = client.GetContainerReference("daas-test-functions");
            CloudBlob b = c.GetBlobReference("test5.txt");

            //Stream s = new WackyStream();
            //b.UploadFromStream(s);
        }


            

        private static void TimerFunc()
        {
            Timer timer = null;

            // More on timers:
            // http://msdn.microsoft.com/en-us/magazine/cc164015.aspx
            TimerCallback t = new TimerCallback(
                obj =>
                {
                    Console.WriteLine(DateTime.Now.ToLongTimeString());
                    timer.Dispose();

                    Console.WriteLine("Done");
                });
            timer = new System.Threading.Timer(t, null, System.Threading.Timeout.Infinite, 1000 * 2);

            
            //Thread.Sleep(5 * 1000);

            timer.Change(0, 1000 * 2);

            Thread.Sleep(5 * 1000);

            timer.Dispose();

            Thread.Sleep(5 * 1000);
        }

        private static void Dict()
        {
            IDictionary<PartitionRowKey, Widget> d = new TableBinder<Widget>(Secrets.GetAccount(), "test5");

            d[new PartitionRowKey { RowKey = "1", PartitionKey = "1" }] = new Widget { Name = "Bob", Score = 100 };
            
        }

        private static void Upload()
        {
            TextWriter tw = Console.Out;

            CloudBlobClient client = Secrets.GetAccount().CreateCloudBlobClient();
            var c = client.GetContainerReference("test3");
            var blob = client.GetBlobReference("up.txt");

            
            

            tw.Write("abc");
            tw.Flush();
        }

     
        static void Listener()
        {
            
        }        
                    
        static void Execute()
        {
            //FunctionInstance instance = GetFunctionInstance();
            //ExecutorClient.Queue(GetAccount(), instance);            
                        
            // Now drain the queue
            var settings = new ExecutionQueueSettings
            {
                Account = Secrets.GetAccount(),
                QueueName = Secrets.ExecutionQueueName
            };

            ExecutorListener l = new ExecutorListener(@"c:\temp\cache", settings);
            l.Run(new EmptyExecutionLogger());
        }               

      
    }



}
