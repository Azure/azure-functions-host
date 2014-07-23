// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.Jobs.Host;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Represents the configuration settings for a <see cref="JobHost"/>.</summary>
    public class JobHostConfiguration : IServiceProvider
    {
        private readonly DefaultStorageAccountProvider _storageAccountProvider;
        private readonly IStorageCredentialsValidator _storageCredentialsValidator =
            new DefaultStorageCredentialsValidator();

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

            // async TODO: Consider moving call out of JobHostConfiguration constructor.
            File.WriteAllText(path, DateTime.UtcNow.ToString("s") + "Z"); // content is not really important, this would help debugging though
        }
    }
}
