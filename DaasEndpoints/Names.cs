using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DaasEndpoints
{
    // Provides common place to list all Azure endpoints.
    // This does not describe the schemas, payloads, etc for those endpoints. 
    public static class EndpointNames
    {
        // Suffix for quickly switching betten production and private runs.
        // Use only lowercase, no numbers, to comply with all the naming restrictions.
        // (queues are only lower, tables are only alphanumeric)
        //private const string prefix = "daaspriv";
        private const string prefix = "daas";

        // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
        public const string FunctionIndexTableName = "DaasFunctionIndex5";

        public const string FunctionInvokeStatsTableName = "DaasFunctionInvokeStats";

        public const string BindersTableName = "SimpleBatchBinders";

        // Where all function instance logging is written.
        // Table is indexed by FunctionInstance.Guid
        public const string FunctionInvokeLogTableName = prefix + "functionlogs";

        // 2ndary table for FunctionInvokeLogTableName, providing an index by time.
        public const string FunctionInvokeLogIndexMru = "functionlogsIndexMRU";
        public const string FunctionInvokeLogIndexMruFunction = "functionlogsIndexMRUByFunction";
        public const string FunctionInvokeLogIndexMruFunctionSucceeded = "functionlogsIndexMRUByFunctionSucceeded";
        public const string FunctionInvokeLogIndexMruFunctionFailed = "functionlogsIndexMRUByFunctionFailed";

        // Queuenames must be all lowercase. 
        public const string ExecutionQueueName = prefix + "-execution";

        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ExecutionLogName = prefix + "-invoke-log";

        // Container where various roles write critical health status. 
        public const string HealthLogContainerName = prefix + "-health-log";

        public const string OrchestratorControlQueue = prefix + "-orch-control";

        public const string FunctionInvokeDoneQueue = prefix + "-invoke-done";

        public const string DaasControlContainerName = prefix + "-control";        
    }

}
