using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web.Http;
using AzureTables;
using RunnerInterfaces;

namespace Monitor.Controllers
{
    public class UsageStatsController : ApiController
    {
        // POST api/values
        public void Post(ExecutionNodeTrackingStats value)
        {
            if (value == null)
            {
                return;
            }
            string accountConnectionString = Secrets.AccountConnectionString;
            var account = Utility.GetAccount(accountConnectionString);
            var table = new AzureTable<ExecutionNodeTrackingStats>(account, "ExecutionNodeStats");
            var rowKey = Utility.GetTickRowKey();

            var partKey = value.AccountName;
            if ((partKey == null) || Regex.IsMatch(partKey, @"[\\\/#\?]"))
            {
                partKey = "unknown";
            }

            table.Write(partKey, rowKey, value);
            table.Flush();
        }  
    }

    public class ExecutionNodeTrackingStats
    {
        public int NumCores { get; set; }
        public float ClockSpeed { get; set; }
        public string OSVersion { get; set; }
        public string AccountName { get; set; }
        public string DeploymentId { get; set; }
    }
}