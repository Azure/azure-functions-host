namespace Dashboard.Data
{
    // Names of tables used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardTableNames
    {
        private const string Prefix = "AzureJobsDashboard";

        public const string FunctionCausalityLog = Prefix + "FunctionCausalityLog";
    }
}
