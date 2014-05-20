using System;
using System.IO;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents the configuration settings for a <see cref="JobHost"/>.</summary>
    public class JobHostConfiguration : IServiceProvider
    {
        private readonly DefaultConnectionStringProvider _connectionStringProvider;
        private readonly IStorageValidator _storageValidator = new DefaultStorageValidator();

        private ITypeLocator _typeLocator = new DefaultTypeLocator();

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        public JobHostConfiguration()
            : this(new DefaultConnectionStringProvider())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="dashboardAndStorageConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public JobHostConfiguration(string dashboardAndStorageConnectionString)
            : this(new DefaultConnectionStringProvider(dashboardAndStorageConnectionString))
        {
        }

        private JobHostConfiguration(DefaultConnectionStringProvider connectionStringProvider)
        {
            _connectionStringProvider = connectionStringProvider;

            WriteSiteExtensionManifest();
        }

        /// <summary>Gets or sets the Azure Storage connection string used for logging and diagnostics.</summary>
        public string DashboardConnectionString
        {
            get { return _connectionStringProvider.DashboardConnectionString; }
            set { _connectionStringProvider.DashboardConnectionString = value; }
        }

        /// <summary>Gets or sets the Azure Storage connection string used for reading and writing data.</summary>
        public string StorageConnectionString
        {
            get { return _connectionStringProvider.StorageConnectionString; }
            set { _connectionStringProvider.StorageConnectionString = value; }
        }

        /// <summary>Gets or sets the Azure Service bus connection string.</summary>
        public string ServiceBusConnectionString
        {
            get { return _connectionStringProvider.ServiceBusConnectionString; }
            set { _connectionStringProvider.ServiceBusConnectionString = value; }
        }

        /// <summary>Gets or sets the type locator.</summary>
        public ITypeLocator TypeLocator
        {
            get { return _typeLocator; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                _typeLocator = value;
            }
        }

        /// <summary>Gets the service object of the specified type.</summary>
        /// <param name="serviceType">The type of service object to get.</param>
        /// <returns>
        /// A service object of the specified type, if one is available; otherwise, <see langword="null"/>.
        /// </returns>
        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IConnectionStringProvider))
            {
                return _connectionStringProvider;
            }
            else if (serviceType == typeof(IStorageValidator))
            {
                return _storageValidator;
            }
            else if (serviceType == typeof(ITypeLocator))
            {
                return _typeLocator;
            }
            else
            {
                return null;
            }
        }

        // When running in Azure Web Sites, write out a manifest file.
        private static void WriteSiteExtensionManifest()
        {
            string jobDataPath = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath);
            if (jobDataPath == null)
            {
                // we're not in Azure Web Sites, bye bye.
                return;
            }

            const string filename = "WebJobsSdk.marker";
            var path = Path.Combine(jobDataPath, filename);

            File.WriteAllText(path, DateTime.UtcNow.ToString("s") + "Z"); // content is not really important, this would help debugging though
        }
    }
}
