namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ Use this
    internal class IndexDriverInput
    {
        // Describes the cloud resource for what to be indexed.
        // This includes a blob download (or upload!) location
        public IndexRequestPayload Request { get; set; }

        // This can be used to download assemblies locally for inspection.
        public string LocalCache { get; set; }
    }
}
