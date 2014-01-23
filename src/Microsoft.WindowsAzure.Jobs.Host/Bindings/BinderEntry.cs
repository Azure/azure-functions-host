namespace Microsoft.WindowsAzure.Jobs
{
    // Result of Binder table. 
    // The partition/row key for the table specifies the Type that this binder applies to.
    // This BinderEntry then specifies where to find the binder.  
    // This is a table that maps types to model binders on the cloud. 
    // Like a cloud-based IOC. 
    internal class BinderEntry
    {
        public string AccountConnectionString { get; set; }
        public string InitType { get; set; }
        public string InitAssembly { get; set; }
        public CloudBlobPath Path { get; set; }
    }
}
