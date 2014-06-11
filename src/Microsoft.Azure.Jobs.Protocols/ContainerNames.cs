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

        /// <summary>The name of the container where host instance abort requests are stored.</summary>
        public const string AbortHostInstanceContainerName = Prefix + "aborts";

        /// <summary>The name of the container where protocol messages from the host are stored.</summary>
        public const string HostOutputContainerName = Prefix + "host-output";

        /// <summary>
        /// The name of the container where protocol messages from the host are archived after processing.
        /// </summary>
        public const string HostArchiveContainerName = Prefix + "host-archive";
    }
}
