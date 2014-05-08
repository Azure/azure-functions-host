namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal static class TableNames
    {
        internal const string Prefix = "AzureJobs";

        public const string EventQueueTableName = Prefix + "EventQueue";

        public const string RunningHostsTableName = Prefix + "RunningHosts";
    }
}
