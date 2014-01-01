
namespace Microsoft.WindowsAzure.Jobs
{
    // Internal test hooks for the host.
    internal class JobHostTestHooks
    {
        // Don't validate the storage accounts passed into the host object.
        // This means the test can set the account to null if the operation truly should not need storage (such as just testing indexing)
        // or it can set it to developer storage if it's just using supported operations. 
        public IStorageValidator StorageValidator { get; set; }

        // Provide the set of types to index.         
        public ITypeLocator TypeLocator { get; set; }
    }
}