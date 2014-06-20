namespace Dashboard.Data
{
    // Names of containers used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardContainerNames
    {
        private const string Prefix = "azure-jobs-dashboard-";

        public const string AbortRequestLogContainerName = Prefix + "aborts";

        public const string FunctionLogContainerName = Prefix + "function-logs";

        public const string RecentFunctionsContainerName = Prefix + "recent-functions";

        public const string FunctionStatisticsContainerName = Prefix + "function-stats";

        public const string HostContainerName = Prefix + "hosts";

        /// <summary>The name of the container where version compatibility warnings are stored.</summary>
        public const string VersionContainerName = Prefix + "versions";
    }
}
