#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Provides well-known table names in the protocol.</summary>
#if PUBLICPROTOCOL
    public static class TableNames
#else
    internal static class TableNames
#endif
    {
        internal const string Prefix = "AzureJobs";

        /// <summary>The name of the table where protocol queue messages are stored.</summary>
        public const string EventQueueTableName = Prefix + "EventQueue";

        /// <summary>The name of the table where host heartbeats are stored.</summary>
        public const string RunningHostsTableName = Prefix + "RunningHosts";
    }
}
