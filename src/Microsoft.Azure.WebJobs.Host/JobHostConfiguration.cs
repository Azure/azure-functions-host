// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>Represents the configuration settings for a <see cref="JobHost"/>.</summary>
    public sealed class JobHostConfiguration : IServiceProvider
    {
        private readonly DefaultHostIdProvider _hostIdProvider;
        private readonly DefaultLoggerProvider _loggerProvider;
        private readonly DefaultStorageAccountProvider _storageAccountProvider;
        private readonly DefaultServiceBusAccountProvider _serviceBusAccountProvider =
            new DefaultServiceBusAccountProvider();
        private readonly JobHostQueuesConfiguration _queueConfiguration = new JobHostQueuesConfiguration();

        private ITypeLocator _typeLocator = new DefaultTypeLocator();
        private INameResolver _nameResolver = new DefaultNameResolver();
        private IFunctionIndexProvider _functionIndexProvider;
        private IBindingProvider _bindingProvider;
        private IExtensionTypeLocator _extensionTypeLocator;
        private ITriggerBindingProvider _triggerBindingProvider;

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
            _hostIdProvider = new DefaultHostIdProvider(() => FunctionIndexProvider, storageAccountProvider);
            _loggerProvider = new DefaultLoggerProvider(storageAccountProvider);
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
            get { return _hostIdProvider.HostId; }
            set { _hostIdProvider.HostId = value; }
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
            get { return _serviceBusAccountProvider.ConnectionString; }
            set { _serviceBusAccountProvider.ConnectionString = value; }
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
                _extensionTypeLocator = null;
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

        private IBindingProvider BindingProvider
        {
            get
            {
                if (_bindingProvider == null)
                {
                    _bindingProvider = DefaultBindingProvider.Create(_nameResolver, _storageAccountProvider,
                        _serviceBusAccountProvider, ExtensionTypeLocator);
                }

                return _bindingProvider;
            }
        }

        private IExtensionTypeLocator ExtensionTypeLocator
        {
            get
            {
                if (_extensionTypeLocator == null)
                {
                    _extensionTypeLocator = new ExtensionTypeLocator(_typeLocator);
                }

                return _extensionTypeLocator;
            }
        }

        private IFunctionIndexProvider FunctionIndexProvider
        {
            get
            {
                if (_functionIndexProvider == null)
                {
                    _functionIndexProvider = new FunctionIndexProvider(TypeLocator, TriggerBindingProvider,
                        BindingProvider);
                }

                return _functionIndexProvider;
            }
        }

        private IHostIdProvider HostIdProvider
        {
            get { return _hostIdProvider; }
        }

        private ITriggerBindingProvider TriggerBindingProvider
        {
            get
            {
                if (_triggerBindingProvider == null)
                {
                    _triggerBindingProvider = DefaultTriggerBindingProvider.Create(_nameResolver,
                        _storageAccountProvider, _serviceBusAccountProvider, ExtensionTypeLocator, HostIdProvider);
                }

                return _triggerBindingProvider;
            }
        }

        /// <summary>Gets the service object of the specified type.</summary>
        /// <param name="serviceType">The type of service object to get.</param>
        /// <returns>
        /// A service object of the specified type, if one is available; otherwise, <see langword="null"/>.
        /// </returns>
        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IBackgroundExceptionDispatcher))
            {
                return BackgroundExceptionDispatcher.Instance;
            }
            else if (serviceType == typeof(IBindingProvider))
            {
                return BindingProvider;
            }
            else if (serviceType == typeof(IFunctionIndexProvider))
            {
                return FunctionIndexProvider;
            }
            else if (serviceType == typeof(IFunctionInstanceLoggerProvider))
            {
                return _loggerProvider;
            }
            else if (serviceType == typeof(IHostIdProvider))
            {
                return HostIdProvider;
            }
            else if (serviceType == typeof(IHostInstanceLoggerProvider))
            {
                return _loggerProvider;
            }
            else if (serviceType == typeof(INameResolver))
            {
                return _nameResolver;
            }
            else if (serviceType == typeof(IQueueConfiguration))
            {
                return _queueConfiguration;
            }
            else if (serviceType == typeof(IStorageAccountProvider))
            {
                return _storageAccountProvider;
            }
            else
            {
                return null;
            }
        }
    }
}
