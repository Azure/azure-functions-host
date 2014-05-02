namespace Microsoft.Azure.Jobs.Host
{
    internal class TestJobHostConfiguration : IJobHostConfiguration
    {
        public IStorageValidator StorageValidator { get; set; }

        public ITypeLocator TypeLocator { get; set; }

        public IConnectionStringProvider ConnectionStringProvider { get; set; }
    }
}
