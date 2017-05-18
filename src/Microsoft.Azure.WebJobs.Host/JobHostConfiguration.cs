// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Represents the configuration settings for a <see cref="JobHost"/>.
    /// </summary>
    public sealed class JobHostConfiguration : IServiceProvider
    {
        private static readonly IConsoleProvider ConsoleProvider = new DefaultConsoleProvider();

        private readonly DefaultStorageAccountProvider _storageAccountProvider;
        private readonly JobHostQueuesConfiguration _queueConfiguration = new JobHostQueuesConfiguration();
        private readonly JobHostBlobsConfiguration _blobsConfiguration = new JobHostBlobsConfiguration();
        private readonly JobHostTraceConfiguration _traceConfiguration = new JobHostTraceConfiguration();
        private readonly ConcurrentDictionary<Type, object> _services = new ConcurrentDictionary<Type, object>();
        private readonly JobHostMetadataProvider _metadataProvider;

        private string _hostId;

        private ServiceProviderWrapper _partialInitServices;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class.
        /// </summary>
        public JobHostConfiguration()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using the
        /// specified connection string for both reading and writing data as well as Dashboard logging.
        /// </summary>
        /// <param name="dashboardAndStorageConnectionString">The Azure Storage connection string to use.
        /// </param>
        public JobHostConfiguration(string dashboardAndStorageConnectionString)
        {
            if (!string.IsNullOrEmpty(dashboardAndStorageConnectionString))
            {
                _storageAccountProvider = new DefaultStorageAccountProvider(this, dashboardAndStorageConnectionString);
            }
            else
            {
                _storageAccountProvider = new DefaultStorageAccountProvider(this);
            }

            Singleton = new SingletonConfiguration();
            Aggregator = new FunctionResultAggregatorConfiguration();

            // add our built in services here
            _metadataProvider = new JobHostMetadataProvider(this);
            IExtensionRegistry extensions = new DefaultExtensionRegistry(_metadataProvider);
            ITypeLocator typeLocator = new DefaultTypeLocator(ConsoleProvider.Out, extensions);
            IConverterManager converterManager = new ConverterManager();
            IWebJobsExceptionHandler exceptionHandler = new WebJobsExceptionHandler();

            AddService<IQueueConfiguration>(_queueConfiguration);
            AddService<IConsoleProvider>(ConsoleProvider);
            AddService<IStorageAccountProvider>(_storageAccountProvider);
            AddService<IExtensionRegistry>(extensions);
            AddService<StorageClientFactory>(new StorageClientFactory());
            AddService<INameResolver>(new DefaultNameResolver());
            AddService<IJobActivator>(DefaultJobActivator.Instance);
            AddService<ITypeLocator>(typeLocator);
            AddService<IConverterManager>(converterManager);
            AddService<IWebJobsExceptionHandler>(exceptionHandler);
            AddService<IFunctionResultAggregatorFactory>(new FunctionResultAggregatorFactory());

            string value = ConfigurationUtility.GetSettingFromConfigOrEnvironment(Host.Constants.EnvironmentSettingName);
            IsDevelopment = string.Compare(Host.Constants.DevelopmentEnvironmentValue, value, StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="JobHost"/> is running in a Development environment.
        /// You can use this property in conjunction with <see cref="UseDevelopmentSettings"/> to default
        /// configuration settings to values optimized for local development.
        /// <Remarks>
        /// The environment is determined by the value of the "AzureWebJobsEnv" environment variable. When this
        /// is set to "Development", this property will return true.
        /// </Remarks>
        /// </summary>
        public bool IsDevelopment { get; private set; }

        /// <summary>
        /// Returns true if <see cref="UseDevelopmentSettings"/> has been called on this instance.
        /// </summary>
        internal bool UsingDevelopmentSettings { get; set; }

        /// <summary>Gets or sets the host ID.</summary>
        /// <remarks>
        /// <para>
        /// All host instances that share the same host ID must be homogeneous. For example, they must use the same
        /// storage accounts and have the same list of functions. Host instances with the same host ID will scale out
        /// and share handling of work such as BlobTrigger and run from dashboard processing and providing a heartbeat
        /// to the dashboard indicating that an instance of the host running.
        /// </para>
        /// <para>
        /// If this value is <see langword="null"/> on startup, a host ID will automatically be generated based on the assembly
        /// name of the first function, and that host ID will be made available via this property after the host has fully started.
        /// </para>
        /// <para>
        /// If non-homogeneous host instances share the same first function assembly,
        /// this property must be set explicitly; otherwise, the host instances will incorrectly try to share work as if
        /// they were homogeneous.
        /// </para>
        /// </remarks>
        public string HostId
        {
            get
            {
                return _hostId;
            }
            set
            {
                if (value != null && !HostIdValidator.IsValid(value))
                {
                    throw new ArgumentException(HostIdValidator.ValidationMessage, "value");
                }

                _hostId = value;
            }
        }

        /// <summary>Gets or sets the job activator.</summary>
        /// <remarks>The job activator creates instances of job classes when calling instance methods.</remarks>
        public IJobActivator JobActivator
        {
            get
            {
                return GetService<IJobActivator>();
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                AddService<IJobActivator>(value);
            }
        }

        /// <summary>
        /// Gets or sets the Azure Storage connection string used for logging and diagnostics.
        /// </summary>
        public string DashboardConnectionString
        {
            get { return _storageAccountProvider.DashboardConnectionString; }
            set { _storageAccountProvider.DashboardConnectionString = value; }
        }

        /// <summary>
        /// Gets or sets the Azure Storage connection string used for reading and writing data.
        /// </summary>
        public string StorageConnectionString
        {
            get { return _storageAccountProvider.StorageConnectionString; }
            set { _storageAccountProvider.StorageConnectionString = value; }
        }

        /// <summary>Gets or sets the type locator.</summary>
        public ITypeLocator TypeLocator
        {
            get
            {
                return GetService<ITypeLocator>();
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                AddService<ITypeLocator>(value);
            }
        }

        /// <summary>
        /// Gets or sets the name resolver used during indexing.
        /// </summary>
        public INameResolver NameResolver
        {
            get
            {
                return GetService<INameResolver>();
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                AddService<INameResolver>(value);
            }
        }

        /// <summary>
        /// Get the converter manager, which can be used to register additional conversions for 
        /// customizing model binding. 
        /// </summary>
        public IConverterManager ConverterManager
        {
            get
            {
                return GetService<IConverterManager>();
            }
        }

        /// <summary>
        /// Gets a helper object for constructing common binding rules for extensions.
        /// </summary>
        public BindingFactory BindingFactory
        {
            get
            {
                var converterManager = this.GetService<IConverterManager>();
                return new BindingFactory(this.NameResolver, converterManager);
            }
        }

        /// <summary>
        /// Gets the configuration used by <see cref="QueueTriggerAttribute"/>.
        /// </summary>
        public JobHostQueuesConfiguration Queues
        {
            get { return _queueConfiguration; }
        }

        /// <summary>
        /// Gets the configuration used by <see cref="BlobTriggerAttribute"/>.
        /// </summary>
        public JobHostBlobsConfiguration Blobs
        {
            get { return _blobsConfiguration; }
        }

        /// <summary>
        /// Gets the configuration used by <see cref="SingletonAttribute"/>.
        /// </summary>
        public SingletonConfiguration Singleton
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the configuration used by the logging aggregator.
        /// </summary>
        public FunctionResultAggregatorConfiguration Aggregator
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the <see cref="ILoggerFactory"/>. 
        /// </summary>
        [CLSCompliant(false)]
        public ILoggerFactory LoggerFactory
        {
            get
            {
                return GetService<ILoggerFactory>();
            }
            set
            {
                AddService<ILoggerFactory>(value);
            }
        }

        /// <summary>
        /// Gets the configuration for event tracing.
        /// </summary>
        public JobHostTraceConfiguration Tracing
        {
            get
            {
                return _traceConfiguration;
            }
        }

        /// <summary>
        /// get host-level metadata which the extension may read to do configuration.
        /// </summary>
        [Obsolete("Not ready for public consumption.")]
        public JObject HostConfigMetadata { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="Host.StorageClientFactory"/> that will be used to create
        /// Azure Storage clients.
        /// </summary>
        [CLSCompliant(false)]
        public StorageClientFactory StorageClientFactory
        {
            get
            {
                return GetService<StorageClientFactory>();
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                AddService<StorageClientFactory>(value);
            }
        }

        /// <summary>
        /// Configures various configuration settings on this <see cref="JobHostConfiguration"/> to
        /// optimize for local development.
        /// </summary>
        public void UseDevelopmentSettings()
        {
            Tracing.ConsoleLevel = TraceLevel.Verbose;
            Queues.MaxPollingInterval = TimeSpan.FromSeconds(2);
            Singleton.ListenerLockPeriod = TimeSpan.FromSeconds(15);

            UsingDevelopmentSettings = true;
        }

        /// <summary>Gets the service object of the specified type.</summary>
        /// <param name="serviceType">The type of service to get.</param>
        /// <returns>
        /// A service of the specified type, if one is available; otherwise, <see langword="null"/>.
        /// </returns>
        public object GetService(Type serviceType)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException("serviceType");
            }

            object service = null;
            _services.TryGetValue(serviceType, out service);

            return service;
        }

        /// <summary>
        /// Gets the service object of the specified type.
        /// </summary>
        /// <typeparam name="TService">The type of service object to get.</typeparam>
        /// <returns>A service of the specified type, if one is available; otherwise, <see langword="null"/>.</returns>
        public TService GetService<TService>()
        {
            return (TService)this.GetService(typeof(TService));
        }

        /// <summary>
        /// Adds the specified service instance, replacing any existing service.
        /// </summary>
        /// <param name="serviceType">The service type</param>
        /// <param name="serviceInstance">The service instance</param>
        public void AddService(Type serviceType, object serviceInstance)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException("serviceType");
            }
            if (!serviceType.IsAssignableFrom(serviceInstance.GetType()))
            {
                throw new ArgumentOutOfRangeException("serviceInstance");
            }

            _services.AddOrUpdate(serviceType, serviceInstance, (key, existingValue) =>
            {
                // always replace existing values
                return serviceInstance;
            });
        }

        /// <summary>
        /// Adds the specified service instance, replacing any existing service.
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <param name="serviceInstance">The service instance</param>
        public void AddService<TService>(TService serviceInstance)
        {
            AddService(typeof(TService), serviceInstance);
        }

        /// <summary>
        /// Add an extension to register new binding attributes and converters.
        /// </summary>
        /// <param name="extension"></param>
        public void AddExtension(IExtensionConfigProvider extension)
        {
            var exts = this.GetExtensions();
            exts.RegisterExtension<IExtensionConfigProvider>(extension);
        }

        internal void AddAttributesFromAssembly(Assembly assembly)
        {
            _metadataProvider.AddAttributesFromAssembly(assembly);
        }

        /// <summary>
        /// Creates a <see cref="IJobHostMetadataProvider"/> for this configuration.
        /// </summary>
        /// <returns>The <see cref="IJobHostMetadataProvider"/>.</returns>
        public IJobHostMetadataProvider CreateMetadataProvider()
        {
            var serviceProvider = this.CreateStaticServices();
            var bindingProvider = serviceProvider.GetService<IBindingProvider>();

            _metadataProvider.Initialize(bindingProvider);

            // Ensure all extensions have been called 

            lock (this)
            {
                if (_partialInitServices == null)
                {
                    _partialInitServices = serviceProvider;
                }
            }

            return _metadataProvider;
        }

        internal ServiceProviderWrapper TakeOwnershipOfPartialInitialization()
        {
            lock (this)
            {
                var ctx = this._partialInitServices;
                this._partialInitServices = null;
                return ctx;
            }
        }
    }
}
