namespace Microsoft.WindowsAzure.Jobs
{
    // Provides common place to list all Azure endpoints.
    // This does not describe the schemas, payloads, etc for those endpoints. 
    internal static class EndpointNames
    {
        // Containers
        private const string containerPrefix = "azure-jobs-";

        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ConsoleOuputLogContainerName = containerPrefix + "invoke-log";

        public const string VersionContainerName = containerPrefix + "versions";

        public const string AbortHostInstanceContainerName = containerPrefix + "aborts";

        // Tables
        // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
        private const string tablePrefix = "AzureJobs";
        
        public const string FunctionIndexTableName = tablePrefix + "FunctionIndex5";

        public const string FunctionInvokeStatsTableName = tablePrefix + "FunctionInvokeStats";

        // Where all function instance logging is written.
        // Table is indexed by FunctionInstance.Guid
        public const string FunctionInvokeLogTableName = tablePrefix + "FunctionLogs";

        // 2ndary table for FunctionInvokeLogTableName, providing an index by time.
        public const string FunctionInvokeLogIndexMru = tablePrefix + "FunctionlogsIndexMRU";
        public const string FunctionInvokeLogIndexMruFunction = tablePrefix + "FunctionlogsIndexMRUByFunction";
        public const string FunctionInvokeLogIndexMruFunctionSucceeded = tablePrefix + "FunctionlogsIndexMRUByFunctionSucceeded";
        public const string FunctionInvokeLogIndexMruFunctionFailed = tablePrefix + "FunctionlogsIndexMRUByFunctionFailed";

        public const string FunctionCausalityLog = tablePrefix + "FunctionCausalityLog";

        public const string HostsTableName = tablePrefix + "Hosts";

        public const string RunningHostsTableName = tablePrefix + "RunningHosts";
    }
}
