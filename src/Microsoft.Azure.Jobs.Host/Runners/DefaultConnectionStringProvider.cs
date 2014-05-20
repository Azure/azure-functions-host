using System;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class DefaultConnectionStringProvider : IConnectionStringProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider = new AmbientConnectionStringProvider();

        private string _dashboardConnectionString;
        private bool _dashboardConnectionStringMayBeNullOrEmpty;
        private string _storageConnectionString;
        private string _serviceBusConnectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        public DefaultConnectionStringProvider()
            : this(_ambientConnectionStringProvider.GetConnectionString(JobHost.DashboardConnectionStringName),
            false, _ambientConnectionStringProvider.GetConnectionString(JobHost.StorageConnectionStringName))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="dashboardAndStorageConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public DefaultConnectionStringProvider(string dashboardAndStorageConnectionString)
            : this(dashboardAndStorageConnectionString, true, dashboardAndStorageConnectionString)
        {
        }

        private DefaultConnectionStringProvider(string dashboardConnectionString,
            bool dashboardConnectionStringMayBeNullOrEmpty, string storageConnectionString)
        {
            _dashboardConnectionString = dashboardConnectionString;
            _dashboardConnectionStringMayBeNullOrEmpty = dashboardConnectionStringMayBeNullOrEmpty;
            _storageConnectionString = storageConnectionString;
        }

        /// <summary>Gets or sets the Azure Storage connection string used for logging and diagnostics.</summary>
        public string DashboardConnectionString
        {
            get { return _dashboardConnectionString; }
            set
            {
                _dashboardConnectionString = value;
                _dashboardConnectionStringMayBeNullOrEmpty = true;
            }
        }

        /// <summary>Gets or sets the Azure Storage connection string used for reading and writing data.</summary>
        public string StorageConnectionString
        {
            get { return _storageConnectionString; }
            set { _storageConnectionString = value; }
        }

        /// <summary>Gets or sets the Azure Service bus connection string.</summary>
        public string ServiceBusConnectionString
        {
            get { return _serviceBusConnectionString; }
            set { _serviceBusConnectionString = value; }
        }

        public string GetConnectionString(string connectionStringName)
        {
            if (connectionStringName == JobHost.DashboardConnectionStringName)
            {
                if (!_dashboardConnectionStringMayBeNullOrEmpty && String.IsNullOrEmpty(_dashboardConnectionString))
                {
                    var msg = JobHost.FormatConnectionStringValidationError("dashboard", JobHost.DashboardConnectionStringName,
                        "Microsoft Azure Storage account connection string is missing or empty.");
                    throw new InvalidOperationException(msg);
                }

                return _dashboardConnectionString;
            }
            else if (connectionStringName == JobHost.StorageConnectionStringName)
            {
                return _storageConnectionString;
            }
            else if (connectionStringName == JobHost.ServiceBusConnectionStringName)
            {
                return _serviceBusConnectionString;
            }
            else
            {
                return _ambientConnectionStringProvider.GetConnectionString(connectionStringName);
            }
        }
   }
}
