using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host
{
    // Names of containers used only by the host (not part of the protocol with the dashboard).
    internal static class HostContainerNames
    {
        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ConsoleOuputLogContainerName = ContainerNames.Prefix + "invoke-log";
    }
}
