namespace Microsoft.WindowsAzure.Jobs
{
    // Context, specific to a given type. 
    // Each type can provide its own configuration
    internal class IndexTypeContext
    {
        public IConfiguration Config { get; set; }
    }
}
