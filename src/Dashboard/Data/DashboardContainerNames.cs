namespace Dashboard.Data
{
    // Names of containers used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardContainerNames
    {
        private const string Prefix = "azure-jobs-dashboard-";

        public const string HostContainer = Prefix + "hosts";

        /// <summary>The name of the container where version compatibility warnings are stored.</summary>
        public const string VersionContainerName = Prefix + "versions";
    }
}
