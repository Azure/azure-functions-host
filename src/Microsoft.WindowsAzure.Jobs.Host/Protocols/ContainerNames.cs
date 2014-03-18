namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    // Provides common place to list all Azure endpoints.
    // This does not describe the schemas, payloads, etc for those endpoints. 
    internal static class ContainerNames
    {
        private const string Prefix = "azure-jobs-";

        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ConsoleOuputLogContainerName = Prefix + "invoke-log";

        public const string VersionContainerName = Prefix + "versions";

        public const string AbortHostInstanceContainerName = Prefix + "aborts";

        public const string EventQueueContainerName = Prefix + "event-queue";
    }
}
