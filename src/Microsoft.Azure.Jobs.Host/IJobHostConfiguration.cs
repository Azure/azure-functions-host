namespace Microsoft.Azure.Jobs.Host
{
    // Internal test hooks for the host.
    internal interface IJobHostConfiguration
    {
        // Don't validate the storage accounts passed into the host object.
        // This means the test can set the account to null if the operation truly should not need storage (such as just testing indexing)
        // or it can set it to developer storage if it's just using supported operations. 
        IStorageValidator StorageValidator { get; }

        // Provide the set of types to index.         
        ITypeLocator TypeLocator { get; }

        // Provide connection strings, normally from config, environment and such. Tests could read from an in-memory hastable.
        IConnectionStringProvider ConnectionStringProvider { get; }
    }
}
