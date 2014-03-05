namespace Microsoft.WindowsAzure.Jobs
{
    // Provides common place to list all Azure endpoints.
    // This does not describe the schemas, payloads, etc for those endpoints. 
    internal static class TableNames
    {
        // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
        private const string Prefix = "AzureJobs";
        
        public const string FunctionIndexTableName = Prefix + "FunctionIndex5";

        public const string FunctionInvokeStatsTableName = Prefix + "FunctionInvokeStats";

        // Where all function instance logging is written.
        // Table is indexed by FunctionInstance.Guid
        public const string FunctionInvokeLogTableName = Prefix + "FunctionLogs";

        // Secondary table for FunctionInvokeLogTableName, providing an index by time.
        public const string FunctionInvokeLogIndexMru = Prefix + "FunctionlogsIndexMRU";
        public const string FunctionInvokeLogIndexMruFunction = Prefix + "FunctionlogsIndexMRUByFunction";
        public const string FunctionInvokeLogIndexMruFunctionSucceeded = Prefix + "FunctionlogsIndexMRUByFunctionSucceeded";
        public const string FunctionInvokeLogIndexMruFunctionFailed = Prefix + "FunctionlogsIndexMRUByFunctionFailed";

        public const string FunctionCausalityLog = Prefix + "FunctionCausalityLog";

        public const string HostsTableName = Prefix + "Hosts";

        public const string RunningHostsTableName = Prefix + "RunningHosts";

        public const string FunctionsInJobIndex = Prefix + "FunctionsInJobIndex";
    }
}
