namespace Dashboard.Data
{
    // Names of tables used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardTableNames
    {
        private const string Prefix = "AzureJobsDashboard";

        public const string FunctionCausalityLog = Prefix + "FunctionCausalityLog";

        public const string FunctionsInJobIndex = Prefix + "FunctionsInJobIndex";

        // NOTE: these exist to give proper legacy data warnings in newer versions. They are not required by current.
        public const string OldFunctionInJobsIndex = "AzureJobsFunctionIndex5";
    }
}
