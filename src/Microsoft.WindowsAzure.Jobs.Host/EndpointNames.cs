namespace Microsoft.WindowsAzure.Jobs
{
    // Provides common place to list all Azure endpoints.
    // This does not describe the schemas, payloads, etc for those endpoints. 
    internal static class EndpointNames
    {
        // Table name is restrictive, must match: "^[A-Za-z][A-Za-z0-9]{2,62}$"
        private const string tablePrefix = "AzureJobs";
        private const string queuePrefix = "azure-jobs";
        private const string containerOrBlobPrefix = "azure-jobs";

        public const string FunctionIndexTableName = tablePrefix + "FunctionIndex5";

        public const string FunctionInvokeStatsTableName = tablePrefix + "FunctionInvokeStats";

        public const string BindersTableName = tablePrefix + "Binders";

        // Where all function instance logging is written.
        // Table is indexed by FunctionInstance.Guid
        public const string FunctionInvokeLogTableName = tablePrefix + "FunctionLogs";

        // 2ndary table for FunctionInvokeLogTableName, providing an index by time.
        public const string FunctionInvokeLogIndexMru = tablePrefix + "FunctionlogsIndexMRU";
        public const string FunctionInvokeLogIndexMruFunction = tablePrefix + "FunctionlogsIndexMRUByFunction";
        public const string FunctionInvokeLogIndexMruFunctionSucceeded = tablePrefix + "FunctionlogsIndexMRUByFunctionSucceeded";
        public const string FunctionInvokeLogIndexMruFunctionFailed = tablePrefix + "FunctionlogsIndexMRUByFunctionFailed";

        public const string FunctionCausalityLog = tablePrefix + "FunctionCausalityLog";

        // Queuenames must be all lowercase. 
        public const string ExecutionQueueName = queuePrefix + "-execution";

        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ConsoleOuputLogContainerName = containerOrBlobPrefix + "-invoke-log";

        // Container where various roles write critical health status. 
        public const string HealthLogContainerName = containerOrBlobPrefix + "-health-log";

        public const string OrchestratorControlQueue = queuePrefix + "-orch-control";

        public const string FunctionInvokeDoneQueue = queuePrefix + "-invoke-done";

        // Key optimization for blob listening: Blob listening on external blobs can have a 30 minute lag. 
        // make it fast to detect blobs that we wrote ourselves. When we write a blob, queueue a notification
        // message.
        public const string BlobWrittenQueue = containerOrBlobPrefix + "-blob-written";

        public const string DaasControlContainerName = containerOrBlobPrefix + "-control";

        public const string RunningHostTableName = tablePrefix + "RunningHost";

        public const string VersionContainerName = containerOrBlobPrefix + "-versions";

        public const string AbortHostInstanceBlobContainerName = containerOrBlobPrefix + "-aborts";

        public const string PrereqTableName = tablePrefix + "SchedPrereqTable";
        public const string SuccessorTableName = tablePrefix + "SchedSuccessorTable";
    }
}
