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
using IndexDriver;
using System.Reflection;
using DaasEndpoints;
using Microsoft.Win32;
using System.Net;
using System.Runtime.InteropServices;
using Utility = RunnerInterfaces.Utility;
using System.Text.RegularExpressions;
using TriggerService;

namespace ConsoleApplication1
{
    class Program
    {
        static Guid New()
        {
            return Guid.NewGuid();
        }

        static void Main()
        {
            //TestIndex();
            //TestAnalytics();

            //Measure();

            // string x = "/mawr/test2/output.txt";
            Listen();

            //Foo();
            //MeasureQueues();

        }

        static void Foo()
        {
            //var acs = LocalRunnerHost.Program.GetAccountInfo(@"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Local.cscfg");
            //CloudStorageAccount account = CloudStorageAccount.Parse(acs.AccountConnectionString);
            CloudStorageAccount account = null;
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("test6");

            // var l = new BlobListener(new CloudBlobContainer[] { container });

            var x = BlobLogListener.GetRecentBlobWrites(blobClient, 5).ToArray();
            foreach (var y in x)
            {
                Console.WriteLine(y);
            }
        }

        private static void MeasureQueues()
        {
            //var acs = LocalRunnerHost.Program.GetAccountInfo(@"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Local.cscfg");
            //CloudStorageAccount account = CloudStorageAccount.Parse(acs.AccountConnectionString);
            CloudStorageAccount account = null;
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
            string containerName = "test9";
            var container = blobClient.GetContainerReference(containerName);


            CloudQueueClient queueClient = account.CreateCloudQueueClient();
            var q = queueClient.GetQueueReference("mytestqueue");

            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            int N = 30;
            int NThreads = 10;

            Thread[] threads = new Thread[NThreads];

            for (int t = 0; t < NThreads; t++)
            {                
                threads[t] = new Thread(_ =>
                    {
                        for (int i = 0; i < N; i++)
                        {
                            q.GetMessage();
                        }
                    });
                threads[t].Start();
            }
            foreach (var thread in threads)
            {
                thread.Join();
            }            

            sw.Stop();

            Console.WriteLine("Timer: {0}, {1}", sw.ElapsedMilliseconds, NThreads);

        }

        private static void Listen()
        {
            //var acs = LocalRunnerHost.Program.GetAccountInfo(@"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Local.cscfg");
            //CloudStorageAccount account = CloudStorageAccount.Parse(acs.AccountConnectionString);
            CloudStorageAccount account = null;
            CloudBlobClient blobClient = account.CreateCloudBlobClient();
           
            string containerName = "test9";
            var container = blobClient.GetContainerReference(containerName);

            BlobLogListener.EnableLogging(blobClient);

            var l = new BlobListener(new CloudBlobContainer[] { container });

            using (TextWriter tw = new StreamWriter(@"c:\temp\log6_pollspeed.txt"))
            {
                Stopwatch sw = new Stopwatch();
                int i = 0;
                while (i < 12 * 50)
                {
                    Utility.WriteBlob(account, containerName, string.Format("z{0}.txt", i), "x");


                    // Count of poll-speed vs. # of callbacks

                    int numCallbacks = 0;

                    var startTime = sw.ElapsedMilliseconds;
                    sw.Start();
                    l.Poll(blob =>
                        {
                            numCallbacks++;
                            sw.Stop();
                            Callback(blob);
                            sw.Start();
                        }
                        );
                    sw.Stop();
                    var deltaMs = sw.ElapsedMilliseconds - startTime;
                    tw.WriteLine("{0},{1},{2},{3}", sw.ElapsedMilliseconds, i, deltaMs, numCallbacks);

                    Thread.Sleep(5 * 1000);
                    i++;
                    Console.WriteLine("  Elasped (ms,#, avg) = {0}, {1}, {2}", sw.ElapsedMilliseconds, i, ((double)sw.ElapsedMilliseconds) / i);
                }
            }

            using (TextWriter tw = new StreamWriter(@"c:\temp\log6.txt"))
            {
                foreach (var name in _lags.Keys)
                {
                    tw.WriteLine("{0}, {1}, {2}", name, _lags[name], reportCount[name]);
                }
            }

        }

        static Dictionary<string, TimeSpan> _lags = new Dictionary<string, TimeSpan>();
        static Dictionary<string, int> reportCount = new Dictionary<string, int>();

        static void Callback(CloudBlob blob)
        {
            var time2 = Utility.GetBlobModifiedUtcTime(blob);
            if (time2 == null)
            {
                Console.WriteLine("**** Missing: {0}", blob.Name);
                return;
            }
            var time = time2.Value;
            var now = DateTime.UtcNow;
            Console.WriteLine("{0}, {1}, {2}, {3}", blob.Name, time, now, now - time);

            if (!_lags.ContainsKey(blob.Name))
            {
                _lags[blob.Name] = (now - time);
                reportCount[blob.Name] = 1;
            }
            else
            {
                reportCount[blob.Name]++; 
            }
        }

        static void Measure()
        {
            //var acs = LocalRunnerHost.Program.GetAccountInfo(@"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Local.cscfg");
            //CloudStorageAccount account = CloudStorageAccount.Parse(acs.AccountConnectionString);
            CloudStorageAccount account = null;
            CloudBlobClient blobClient = account.CreateCloudBlobClient();


            int N = 2000;
            DateTime[] write = new DateTime[N];
            DateTime[] found = new DateTime[N];
            bool[] hit = new bool[N];

            using (TextWriter tw = new StreamWriter(@"c:\temp\log3.txt"))
            {
                for (int i = 0; i < N; i++)
                {
                    Utility.WriteBlob(account, "test5", i.ToString() + ".xyz", DateTime.Now.ToString());
                    write[i] = DateTime.UtcNow;
                    Thread.Sleep(10 * 1000);

                    foreach (var row in GetBlobWrites(blobClient, DateTime.MinValue, DateTime.MaxValue))
                    {
                        if (row.ServiceType != ServiceType.Blob)
                        {
                            continue;
                        }

                        {
                            Regex r = new Regex(@"/mawr/test5/(.+)\.xyz");
                            var m = r.Match(row.RequestedObjectKey);
                            if (m.Groups.Count > 1)
                            {
                                var name = m.Groups[1].Value;
                                int idx = int.Parse(name);

                                if (hit[idx] == false)
                                {
                                    hit[idx] = true;

                                    found[idx] = DateTime.UtcNow;
                                    tw.WriteLine("{3}, {0}, {1}, {2}", write[idx], found[idx], found[idx] - write[idx], idx);

                                    Console.WriteLine("{0}, {1}, {2}", write[idx], found[idx], found[idx] - write[idx]);
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("Summary");
            for (int i = 0; i < N; i++)
            {
                Console.WriteLine("{0}, {1}", write[i], found[i]);
            }    
        }

        private static IEnumerable<LogRow> GetBlobWrites(CloudBlobClient blobClient, DateTime startTimeForSearch, DateTime endTimeForSearch)
        {
            var time = DateTime.UtcNow; // will scan back 2 hours, which is enough to deal with clock sqew
            foreach (var blob in BlobLogListener.ListLogFiles(blobClient, "blob", startTimeForSearch, endTimeForSearch))
            {
                foreach (var row in BlobLogListener.ParseLog(blob))
                {
                    yield return row;
                }
            }
        }

        static void TestAnalytics()
        {
            //var acs = LocalRunnerHost.Program.GetAccountInfo(@"C:\CodePlex\azuresimplebatch\DaasService\ServiceConfiguration.Local.cscfg");
            //CloudStorageAccount account = CloudStorageAccount.Parse(acs.AccountConnectionString);
            CloudStorageAccount account = null;
            CloudBlobClient blobClient = account.CreateCloudBlobClient();

            foreach (var blob in BlobLogListener.ListLogFiles(blobClient, "blob", DateTime.MinValue, DateTime.MaxValue))
            {
                //string content = blob.DownloadText();
                //Console.WriteLine(content);
                foreach (var row in BlobLogListener.ParseLog(blob))
                {
                    Console.WriteLine(row.RequestedObjectKey);
                }
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
                    

            //string dir = @"C:\CodePlex\azuresimplebatch\Temp\TestApp1\bin\Debug";
            string dir = @"C:\TfsOnline\SimpleBatchOps\GalDiff\GalDiff\bin\debug";
            i.IndexLocalDir(funcApplyLocation, dir);
        }

        class FuncTable : IFunctionTable
        {
            public void Add(FunctionDefinition func)
            {
            }

            public void Delete(FunctionDefinition func)
            {
            }

            public FunctionDefinition Lookup(string functionId)
            {
                throw new NotImplementedException();
            }

            public FunctionDefinition[] ReadAll()
            {
                return new FunctionDefinition[0];
            }
        }
    }
}
