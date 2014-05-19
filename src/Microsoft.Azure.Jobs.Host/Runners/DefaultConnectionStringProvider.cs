using System;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class DefaultConnectionStringProvider : IConnectionStringProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider = new AmbientConnectionStringProvider();

        private string _dataConnectionString;
        private string _runtimeConnectionString;
        private bool _runtimeConnectionStringMayBeNullOrEmpty;
        private string _serviceBusConnectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        public DefaultConnectionStringProvider()
            : this(_ambientConnectionStringProvider.GetConnectionString(JobHost.DataConnectionStringName),
            _ambientConnectionStringProvider.GetConnectionString(JobHost.LoggingConnectionStringName),
            false,
            _ambientConnectionStringProvider.GetConnectionString(JobHost.ServiceBusConnectionStringName))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="dataAndRuntimeConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public DefaultConnectionStringProvider(string dataAndRuntimeConnectionString)
            : this(dataAndRuntimeConnectionString, dataAndRuntimeConnectionString, true,
            _ambientConnectionStringProvider.GetConnectionString(JobHost.ServiceBusConnectionStringName))
        {
        }

        private DefaultConnectionStringProvider(string dataConnectionString, string runtimeConnectionString,
            bool runtimeConnectionStringMayBeNullOrEmpty, string serviceBusConnectionString)
        {
            _dataConnectionString = dataConnectionString;
            _runtimeConnectionString = runtimeConnectionString;
            _runtimeConnectionStringMayBeNullOrEmpty = runtimeConnectionStringMayBeNullOrEmpty;
            _serviceBusConnectionString = serviceBusConnectionString;
        }

        /// <summary>Gets or sets the Azure Storage connection string used for reading and writing data.</summary>
        public string DataConnectionString
        {
            get { return _dataConnectionString; }
            set { _dataConnectionString = value; }
        }

        /// <summary>Gets or sets the Azure Storage connection string used for logging and diagnostics.</summary>
        public string RuntimeConnectionString
        {
            get { return _runtimeConnectionString; }
            set
            {
                _runtimeConnectionString = value;
                _runtimeConnectionStringMayBeNullOrEmpty = true;
            }
        }

        /// <summary>Gets or sets the Azure Service bus connection string.</summary>
        public string ServiceBusConnectionString
        {
            get { return _serviceBusConnectionString; }
            set { _serviceBusConnectionString = value; }
        }

        public string GetConnectionString(string connectionStringName)
        {
            if (connectionStringName == JobHost.DataConnectionStringName)
            {
                return _dataConnectionString;
            }
            else if (connectionStringName == JobHost.LoggingConnectionStringName)
            {
                if (!_runtimeConnectionStringMayBeNullOrEmpty && String.IsNullOrEmpty(_runtimeConnectionString))
                {
                    var msg = JobHost.FormatConnectionStringValidationError("runtime", JobHost.LoggingConnectionStringName,
                        "Microsoft Azure Storage account connection string is missing or empty.");
                    throw new InvalidOperationException(msg);
                }

                return _runtimeConnectionString;
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