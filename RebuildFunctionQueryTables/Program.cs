using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AzureTables;
using DaasEndpoints;
using Executor;
using RunnerInterfaces;
using SimpleBatch;

namespace RebuildFunctionQueryTables
{
    // Rebuild the secondary indices
    class Program
    {
        static void Main(string[] args)
        {
            string configPath = args[0];
            IAccountInfo accountInfo = LocalRunnerHost.Program.GetAccountInfo(configPath);
            Services s = new Services(accountInfo);
            var account = accountInfo.GetAccount();

            // Expects that old tables are deleted

            Utility.DeleteTable(account, EndpointNames.FunctionInvokeLogIndexMru);
            Utility.DeleteTable(account, EndpointNames.FunctionInvokeLogIndexMruFunction);
            Utility.DeleteTable(account, EndpointNames.FunctionInvokeLogIndexMruFunctionFailed);
            Utility.DeleteTable(account, EndpointNames.FunctionInvokeLogIndexMruFunctionSucceeded);
            Utility.DeleteTable(account, EndpointNames.FunctionInvokeStatsTableName);

            Stopwatch sw = Stopwatch.StartNew();

            IFunctionCompleteLogger f = s.GetStatsAggregator();

            IAzureTable<ExecutionInstanceLogEntity> table = new AzureTable<ExecutionInstanceLogEntity>(account, EndpointNames.FunctionInvokeLogTableName);

            var logs = table.Enumerate();

            int count = 0;

            foreach (ExecutionInstanceLogEntity log in logs)
            {
                f.IndexCompletedFunction(log);
                count++;
                if (count % 100 == 0)
                {
                    f.Flush();
                    Console.Write(".");
                }
                if (count % 2000 == 0)
                {
                    Console.WriteLine("time: {0}", sw.Elapsed);            
                }
            }
            f.Flush();

            sw.Stop();

            Console.WriteLine();
            Console.WriteLine("Done! Wrote {0} logs", count);
            Console.WriteLine("time: {0}", sw.Elapsed);            
        }
    }
}
