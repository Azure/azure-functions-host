using System;
using System.Diagnostics.Contracts;
using System.Globalization;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class ServiceProviderExtensions
    {
        public static IConnectionStringProvider GetConnectionStringProvider(this IServiceProvider serviceProvider)
        {
            return GetService<IConnectionStringProvider>(serviceProvider);
        }

        public static IStorageValidator GetStorageValidator(this IServiceProvider serviceProvider)
        {
            return GetService<IStorageValidator>(serviceProvider);
        }

        public static ITypeLocator GetTypeLocator(this IServiceProvider serviceProvider)
        {
            return GetService<ITypeLocator>(serviceProvider);
        }

        private static T GetService<T>(this IServiceProvider serviceProvider) where T : class
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException("serviceProvider");
            }

            object service = serviceProvider.GetService(typeof(T));

            if (service == null)
            {
                return null;
            }

            T typedService = service as T;

            if (typedService == null)
            {
                string message = String.Format(CultureInfo.InvariantCulture,
                    "ServiceProvider.GetService({0}) must return an object assignable to {0}.", typeof(T).Name);
                throw new InvalidOperationException(message);
            }

            return typedService;
        }
    }
}
