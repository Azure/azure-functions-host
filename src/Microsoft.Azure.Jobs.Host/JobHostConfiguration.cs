using System;
using System.IO;
using Microsoft.Azure.Jobs.Host;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents the configuration settings for a <see cref="JobHost"/>.</summary>
    public class JobHostConfiguration : IJobHostConfiguration, IConnectionStringProvider
    {
        private static readonly IConnectionStringProvider _ambientConnectionStringProvider = new DefaultConnectionStringProvider();

        private readonly IStorageValidator _storageValidator = new DefaultStorageValidator();

        private string _dataConnectionString;
        private string _runtimeConnectionString;
        private bool _runtimeConnectionStringMayBeNullOrEmpty;
        private ITypeLocator _typeLocator = new DefaultTypeLocator();

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        public JobHostConfiguration()
            : this(_ambientConnectionStringProvider.GetConnectionString(JobHost.DataConnectionStringName),
            _ambientConnectionStringProvider.GetConnectionString(JobHost.LoggingConnectionStringName),
            false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        /// <param name="dataAndRuntimeConnectionString">
        /// The Azure Storage connection string for accessing data and logging.
        /// </param>
        public JobHostConfiguration(string dataAndRuntimeConnectionString)
            : this(dataAndRuntimeConnectionString, dataAndRuntimeConnectionString, true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using one Microsoft Azure
        /// Storage connection strings for reading and writing data and another connection string for logging.
        /// </summary>
        /// <param name="dataConnectionString">The Azure Storage connection string for accessing data.</param>
        /// <param name="runtimeConnectionString">The Azure Storage connection string for accessing logging.</param>
        public JobHostConfiguration(string dataConnectionString, string runtimeConnectionString)
            : this(dataConnectionString, runtimeConnectionString, true)
        {
        }

        private JobHostConfiguration(string dataConnectionString, string runtimeConnectionString,
            bool runtimeConnectionStringMayBeNullOrEmpty)
        {
            WriteAntaresManifest();

            _dataConnectionString = dataConnectionString;
            _runtimeConnectionString = runtimeConnectionString;
            _runtimeConnectionStringMayBeNullOrEmpty = runtimeConnectionStringMayBeNullOrEmpty;
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

        /// <summary>Gets or sets the type locator.</summary>
        public ITypeLocator TypeLocator
        {
            get { return _typeLocator; }
            set { _typeLocator = value; }
        }

        IStorageValidator IJobHostConfiguration.StorageValidator
        {
            get { return _storageValidator; }
        }

        IConnectionStringProvider IJobHostConfiguration.ConnectionStringProvider
        {
            get { return this; }
        }

        string IConnectionStringProvider.GetConnectionString(string connectionStringName)
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
            else
            {
                return _ambientConnectionStringProvider.GetConnectionString(connectionStringName);
            }
        }

        // When running in Antares, write out a manifest file.
        private static void WriteAntaresManifest()
        {
            string jobDataPath = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath);
            if (jobDataPath == null)
            {
                // we're not in antares, bye bye.
                return;
            }

            const string filename = "WebJobsSdk.marker";
            var path = Path.Combine(jobDataPath, filename);

            File.WriteAllText(path, DateTime.UtcNow.ToString("s") + "Z"); // content is not really important, this would help debugging though
        }
    }
}
