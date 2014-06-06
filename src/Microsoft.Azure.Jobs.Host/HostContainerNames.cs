using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host
{
    // Names of containers used only by the host (not directly part of the protocol with the dashboard, though other
    // parts may point to blobs stored here).
    internal static class HostContainerNames
    {
        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ConsoleOutputLogContainerName = ContainerNames.Prefix + "invoke-log";

        public const string HeartbeatContainerName = ContainerNames.Prefix + "host-heartbeats";
    }
}
