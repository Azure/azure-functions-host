using System;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    public class IndexOperation
    {
        // User account that the Blobpath is resolved against
        public string UserAccountConnectionString { get; set; }

        public string Blobpath { get; set; }
    }

    public class DeleteOperation
    {
        public string FunctionToDelete { get; set; }
    }

    // Request that APIs at the given Url be indexed.
    public class IndexUrlOperation
    {
        public string Url { get; set; }
    }

    // Queue message payload to request that orchestrator rescan a blob path
    public class IndexRequestPayload
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