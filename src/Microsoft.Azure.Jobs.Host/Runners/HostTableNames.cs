using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    // Names of tables used only by the host (not part of the protocol with the dashboard).
    internal static class HostTableNames
    {
        public const string HostsTableName = TableNames.Prefix + "Hosts";
    }
}
