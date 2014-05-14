#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Provides well-known container names in the protocol.</summary>
#if PUBLICPROTOCOL
    public static class ContainerNames
#else
    internal static class ContainerNames
#endif
    {
        internal const string Prefix = "azure-jobs-";

        /// <summary>The name of the container where version compatibility warnings are stored.</summary>
        public const string VersionContainerName = Prefix + "versions";

        /// <summary>The name of the container where host instance abort requests are stored.</summary>
        public const string AbortHostInstanceContainerName = Prefix + "aborts";

        /// <summary>The name of the container where protocol queue message payloads are stored.</summary>
        public const string EventQueueContainerName = Prefix + "event-queue";
    }
}
