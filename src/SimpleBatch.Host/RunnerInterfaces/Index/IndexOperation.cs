namespace Microsoft.WindowsAzure.Jobs
{
    internal class IndexOperation
    {
        // User account that the Blobpath is resolved against
        public string UserAccountConnectionString { get; set; }

        public string Blobpath { get; set; }
    }
}
