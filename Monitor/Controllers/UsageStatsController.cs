using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web.Http;
using AzureTables;
using RunnerInterfaces;
using SimpleBatch;

namespace Monitor.Controllers
{
    public class UsageStatsController : ApiController
    {
        public static IAzureTable<ExecutionNodeTrackingStats> GetTable()
        {
            string accountConnectionString = Secrets.AccountConnectionString;
            var account = Utility.GetAccount(accountConnectionString);
            var table = new AzureTable<ExecutionNodeTrackingStats>(account, "ExecutionNodeStats");

            return table;
        }

        // POST api/values
        public void Post(ExecutionNodeTrackingStats value)
        {
            if (value == null)
            {
                return;
            }
            var table = GetTable();

            var rowKey = Utility.GetTickRowKey();

            var partKey = value.AccountName;
            if ((partKey == null) || Regex.IsMatch(partKey, @"[\\\/#\?]"))
            {
                partKey = "unknown";
            }

            value.ComputeVmSize();

            table.Write(partKey, rowKey, value);
            table.Flush();
        }
    }

    public class ExecutionNodeTrackingStats
    {
        // Size of VM instances. In XS hours so that we have integral values. 
        // Small is 6 XS hours. 
        public int VMExtraSmallSize { get; set; } // computed from incoming 

        public int NumCores { get; set; }
        public ulong PhysicalMemory { get; set; }

        public string OSVersion { get; set; }
        public string AccountName { get; set; }

        public string DeploymentId { get; set; }

        public void ComputeVmSize()
        {
            int xsmallHours = ComputeVmSize(NumCores, PhysicalMemory);
            this.VMExtraSmallSize = xsmallHours;
        }

        // Compute a VM size (in XSmall hours).
        static int ComputeVmSize(int numCores, ulong physicalMemoryBytes)
        {
            // See http://www.windowsazure.com/en-us/pricing/details/#header-2
            if (numCores > 1)
            {
                return numCores * 6;
            }
            // Is it Small vs. XSmall?
            if (physicalMemoryBytes < 803880961)
            {
                return 1; // XSmall
            }
            return 6; // Small
        }
    }
}