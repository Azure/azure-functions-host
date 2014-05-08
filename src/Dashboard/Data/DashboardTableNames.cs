using Microsoft.Azure.Jobs.Host.Protocols;

namespace Dashboard.Data
{
    // Names of tables used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardTableNames
    {
        public const string FunctionCausalityLog = TableNames.Prefix + "FunctionCausalityLog";

        public const string FunctionIndexTableName = TableNames.Prefix + "FunctionIndex5";

        public const string FunctionsInJobIndex = TableNames.Prefix + "FunctionsInJobIndex";

        public const string FunctionInvokeLogIndexMru = TableNames.Prefix + "FunctionlogsIndexMRU";
        public const string FunctionInvokeLogIndexMruFunction = TableNames.Prefix + "FunctionlogsIndexMRUByFunction";
        public const string FunctionInvokeLogIndexMruFunctionSucceeded = TableNames.Prefix + "FunctionlogsIndexMRUByFunctionSucceeded";
        public const string FunctionInvokeLogIndexMruFunctionFailed = TableNames.Prefix + "FunctionlogsIndexMRUByFunctionFailed";

        public const string FunctionInvokeStatsTableName = TableNames.Prefix + "FunctionInvokeStats";
    }
}
