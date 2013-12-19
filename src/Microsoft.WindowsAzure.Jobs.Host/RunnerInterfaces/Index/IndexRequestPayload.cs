namespace Microsoft.WindowsAzure.Jobs
{
    // Queue message payload to request that orchestrator rescan a blob path
    internal class IndexRequestPayload
    {
        // Account that the service is using.
        // This is where the function entries are written.
        public string ServiceAccountConnectionString { get; set; }

        // URI to upload to with index results.
        // Use string instead of URI type to avoid escaping. That confuses azure.
        public string Writeback { get; set; }

        // Payload operation. E.g. Index, Delete, etc.
        public object Operation { get; set; }
    }
}
