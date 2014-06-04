namespace Dashboard.Data
{
    // Names of tables used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardTableNames
    {
        private const string Prefix = "AzureJobsDashboard";

        public const string FunctionCausalityLog = Prefix + "FunctionCausalityLog";

        public const string FunctionInvokeLogTableName = Prefix + "FunctionLogs";

        public const string FunctionsInJobIndex = Prefix + "FunctionsInJobIndex";

        public const string FunctionInvokeLogIndexMru = Prefix + "FunctionlogsIndexMRU";
        public const string FunctionInvokeLogIndexMruFunction = Prefix + "FunctionlogsIndexMRUByFunction";
        public const string FunctionInvokeLogIndexMruFunctionSucceeded = Prefix + "FunctionlogsIndexMRUByFunctionSucceeded";
        public const string FunctionInvokeLogIndexMruFunctionFailed = Prefix + "FunctionlogsIndexMRUByFunctionFailed";

        public const string FunctionInvokeStatsTableName = Prefix + "FunctionInvokeStats";
    }
}
