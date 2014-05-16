namespace Dashboard.Data
{
    // Names of containers used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardContainerNames
    {
        private const string Prefix = "azure-jobs";

        public const string HostContainer = Prefix + "hosts";
    }
}
