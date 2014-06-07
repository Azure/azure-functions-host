using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host
{
    // Names of tables used only by the host (not part of the protocol with the dashboard).
    internal static class HostTableNames
    {
        private const string Prefix = "AzureJobs";

        public const string HostsTableName = Prefix + "Hosts";
    }
}
