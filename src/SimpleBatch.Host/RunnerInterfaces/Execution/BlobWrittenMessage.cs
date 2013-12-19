namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ ISn't th
    // $$$ USe CloudBlobDescriptor? AccountName vs. AccountConnectionString
    internal class BlobWrittenMessage
    {
        public string AccountName { get; set; }
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
    }
}
