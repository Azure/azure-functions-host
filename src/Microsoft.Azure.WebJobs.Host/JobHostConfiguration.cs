// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Queues;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>Represents the configuration settings for a <see cref="JobHost"/>.</summary>
    public sealed class JobHostConfiguration : IServiceProvider
    {
        private readonly DefaultStorageAccountProvider _storageAccountProvider;
        private readonly IStorageCredentialsValidator _storageCredentialsValidator =
            new DefaultStorageCredentialsValidator();
        private readonly JobHostQueuesConfiguration _queueConfiguration = new JobHostQueuesConfiguration();

        private string _hostId;
        private ITypeLocator _typeLocator = new DefaultTypeLocator();
        private INameResolver _nameResolver = new DefaultNameResolver();

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using a single Microsoft Azure
        /// Storage connection string for both reading and writing data as well as logging.
        /// </summary>
        public JobHostConfiguration()
            : this(new DefaultStorageAccountProvider())
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
            : this(new DefaultStorageAccountProvider(dashboardAndStorageConnectionString))
        {
        }

        private JobHostConfiguration(DefaultStorageAccountProvider storageAccountProvider)
        {
            _storageAccountProvider = storageAccountProvider;

            WriteSiteExtensionManifest();
        }

        /// <summary>Gets or sets the host ID.</summary>
        /// <remarks>
        /// <para>
        /// All host instances that share the same host ID must be homogeneous. For example, they must use the same
        /// storage accounts and have the same list of functions. Host instances with the same host ID will scale out
        /// and share handling of work such as BlobTrigger and run from dashboard processing and providing a heartbeat
        /// to the dashboard indicating that an instance of the host running.
        /// </para>
        /// <para>
        /// If this value is <see langword="null"/>, a host ID will automatically be generated based on the assembly
        /// name of the first function.
        /// </para>
        /// <para>
        /// If non-homogeneous host instances share the same first function assembly,
        /// this property must be set explicitly; otherwise, the host instances will incorectly try to share work as if
        /// they were homogeneous.
        /// </para>
        /// </remarks>
        public string HostId
        {
            get { return _hostId; }
            set
            {
                if (value != null && !HostIdValidator.IsValid(value))
                {
                    throw new ArgumentException(HostIdValidator.ValidationMessage, "value");
                }

                _hostId = value;
            }
        }

        /// <summary>Gets or sets the Azure Storage connection string used for logging and diagnostics.</summary>
        public string DashboardConnectionString
        {
            get { return _storageAccountProvider.DashboardConnectionString; }
            set { _storageAccountProvider.DashboardConnectionString = value; }
        }

        /// <summary>Gets or sets the Azure Storage connection string used for reading and writing data.</summary>
        public string StorageConnectionString
        {
            get { return _storageAccountProvider.StorageConnectionString; }
            set { _storageAccountProvider.StorageConnectionString = value; }
        }

        /// <summary>Gets or sets the Azure Service bus connection string.</summary>
        public string ServiceBusConnectionString
        {
            get { return _storageAccountProvider.ServiceBusConnectionString; }
            set { _storageAccountProvider.ServiceBusConnectionString = value; }
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

        /// <summary>
        /// Gets or sets the name resolver used during indexing. 
        /// </summary>
        public INameResolver NameResolver
        {
            get { return _nameResolver; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                _nameResolver = value;
            }
        }

        /// <summary>Gets the configuration used by <see cref="QueueTriggerAttribute"/>.</summary>
        public JobHostQueuesConfiguration Queues
        {
            get { return _queueConfiguration; }
        }

        /// <summary>Gets the service object of the specified type.</summary>
        /// <param name="serviceType">The type of service object to get.</param>
        /// <returns>
        /// A service object of the specified type, if one is available; otherwise, <see langword="null"/>.
        /// </returns>
        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IStorageAccountProvider))
            {
                return _storageAccountProvider;
            }
            else if (serviceType == typeof(IStorageCredentialsValidator))
            {
                return _storageCredentialsValidator;
            }
            else if (serviceType == typeof(IConnectionStringProvider))
            {
                return _storageAccountProvider;
            }
            else if (serviceType == typeof(ITypeLocator))
            {
                return _typeLocator;
            }
            else if (serviceType == typeof(INameResolver))
            {
                return _nameResolver;
            }
            else if (serviceType == typeof(IQueueConfiguration))
            {
                return _queueConfiguration;
            }
            else if (serviceType == typeof(IHostIdProvider))
            {
                return GetHostIdProvider();
            }
            else
            {
                return null;
            }
        }

        private IHostIdProvider GetHostIdProvider()
        {
            if (_hostId != null)
            {
                return new FixedHostIdProvider(_hostId);
            }
            else
            {
                return new DynamicHostIdProvider(_storageAccountProvider.GetAccount(ConnectionStringNames.Storage));
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

            // async TODO: Consider moving call out of JobHostConfiguration constructor.
            File.WriteAllText(path, DateTime.UtcNow.ToString("s") + "Z"); // content is not really important, this would help debugging though
        }
    }
}
