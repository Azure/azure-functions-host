using System;

namespace Microsoft.Azure.Jobs.Host
{
    internal class TestJobHostConfiguration : IServiceProvider
    {
        public IConnectionStringProvider ConnectionStringProvider { get; set; }

        public IStorageValidator StorageValidator { get; set; }

        public ITypeLocator TypeLocator { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IConnectionStringProvider))
            {
                return ConnectionStringProvider;
            }
            else if (serviceType == typeof(IStorageValidator))
            {
                return StorageValidator;
            }
            else if (serviceType == typeof(ITypeLocator))
            {
                return TypeLocator;
            }
            else
            {
                return null;
            }
        }
    }
}
