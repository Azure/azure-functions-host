// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Bindings;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Tables;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal static class JobHostConfigurationExtensions
    {
        // Static initialization. Returns a service provider with some new services initialized. 
        // The new services:
        // - can retrieve static config like binders and converters; but the listeners haven't yet started.
        // - can be flowed into the runtime initialization to get a JobHost spun up and running.
        // This is just static initialization and should not need to make any network calls, 
        // and so this method should not need to be async. 
        // This can be called multiple times on a config, which is why it returns a new ServiceProviderWrapper
        // instead of modifying the config.
        public static ServiceProviderWrapper CreateStaticServices(this JobHostConfiguration config)
        {
            var services = new ServiceProviderWrapper(config);

            var consoleProvider = services.GetService<IConsoleProvider>();
            var nameResolver = services.GetService<INameResolver>();
            IWebJobsExceptionHandler exceptionHandler = services.GetService<IWebJobsExceptionHandler>();
            IQueueConfiguration queueConfiguration = services.GetService<IQueueConfiguration>();
            var blobsConfiguration = config.Blobs;

            IStorageAccountProvider storageAccountProvider = services.GetService<IStorageAccountProvider>();
            IBindingProvider bindingProvider = services.GetService<IBindingProvider>();
            SingletonManager singletonManager = services.GetService<SingletonManager>();

            IHostIdProvider hostIdProvider = services.GetService<IHostIdProvider>();
            var hostId = config.HostId;
            if (hostId != null)
            {
                hostIdProvider = new FixedHostIdProvider(hostId);
            }

            if (hostIdProvider == null)
            {
                // Need a deferred getter since the IFunctionIndexProvider service isn't created until later. 
                Func<IFunctionIndexProvider> deferredGetter = () => services.GetService<IFunctionIndexProvider>();
                hostIdProvider = new DynamicHostIdProvider(storageAccountProvider, deferredGetter);
            }
            services.AddService<IHostIdProvider>(hostIdProvider);

            AzureStorageDeploymentValidator.Validate();

            IExtensionTypeLocator extensionTypeLocator = services.GetService<IExtensionTypeLocator>();
            if (extensionTypeLocator == null)
            {
                extensionTypeLocator = new ExtensionTypeLocator(services.GetService<ITypeLocator>());
                services.AddService<IExtensionTypeLocator>(extensionTypeLocator);
            }

            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor = new ContextAccessor<IMessageEnqueuedWatcher>();
            services.AddService(messageEnqueuedWatcherAccessor);
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor = new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();

            // Create a wrapper TraceWriter that delegates to both the user 
            // TraceWriters specified via config (if present), as well as to Console
            TraceWriter trace = new ConsoleTraceWriter(config.Tracing, consoleProvider.Out);
            services.AddService<TraceWriter>(trace);

            // Add built-in extensions 
            var metadataProvider = new JobHostMetadataProvider();
            metadataProvider.AddAttributesFromAssembly(typeof(TableAttribute).Assembly);

            var exts = config.GetExtensions();
            bool builtinsAdded = exts.GetExtensions<IExtensionConfigProvider>().OfType<TableExtension>().Any();
            if (!builtinsAdded)
            {
                config.AddExtension(new TableExtension());
                config.AddExtension(new QueueExtension());
            }

            ExtensionConfigContext context = new ExtensionConfigContext
            {
                Config = config,
                Trace = trace,
                PerHostServices = services
            };
            InvokeExtensionConfigProviders(context);

            // After this point, all user configuration has been set. 

            if (singletonManager == null)
            {
                var logger = config.LoggerFactory?.CreateLogger(LogCategories.Singleton);

                IDistributedLockManager lockManager = services.GetService<IDistributedLockManager>();
                if (lockManager == null)
                {
                    lockManager = new BlobLeaseDistributedLockManager(
                        storageAccountProvider,
                        trace,
                        logger);
                    services.AddService<IDistributedLockManager>(lockManager);
                }

                singletonManager = new SingletonManager(
                    lockManager,                     
                    config.Singleton, 
                    trace,
                    exceptionHandler,
                    config.LoggerFactory, 
                    hostIdProvider, 
                    services.GetService<INameResolver>());
                services.AddService<SingletonManager>(singletonManager);
            }

            IExtensionRegistry extensions = services.GetExtensions();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, extensionTypeLocator, hostIdProvider, queueConfiguration, blobsConfiguration, exceptionHandler,
                messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor, sharedContextProvider, extensions, singletonManager, trace, config.LoggerFactory);
            services.AddService<ITriggerBindingProvider>(triggerBindingProvider);

            if (bindingProvider == null)
            {
                bindingProvider = DefaultBindingProvider.Create(nameResolver, config.LoggerFactory, storageAccountProvider, extensionTypeLocator, blobWrittenWatcherAccessor, extensions);
                services.AddService<IBindingProvider>(bindingProvider);
            }
                        
            var converterManager = (ConverterManager)config.ConverterManager;
            metadataProvider.Initialize(bindingProvider, converterManager, exts);
            services.AddService<IJobHostMetadataProvider>(metadataProvider);

            return services;
        }

        // Do the full runtime intitialization. This includes static initialization. 
        // This mainly means:
        // - indexing the functions 
        // - spinning up the listeners (so connecting to the services)
        public static async Task<JobHostContext> CreateJobHostContextAsync(
            this JobHostConfiguration config, 
            ServiceProviderWrapper services, // Results from first phase
            JobHost host, 
            CancellationToken shutdownToken, 
            CancellationToken cancellationToken)
        {
            FunctionExecutor functionExecutor = services.GetService<FunctionExecutor>();
            IFunctionIndexProvider functionIndexProvider = services.GetService<IFunctionIndexProvider>();
            ITriggerBindingProvider triggerBindingProvider = services.GetService<ITriggerBindingProvider>();
            IBindingProvider bindingProvider = services.GetService<IBindingProvider>();
            SingletonManager singletonManager = services.GetService<SingletonManager>();
            IJobActivator activator = services.GetService<IJobActivator>();
            IHostIdProvider hostIdProvider = services.GetService<IHostIdProvider>();
            INameResolver nameResolver = services.GetService<INameResolver>();
            IExtensionRegistry extensions = services.GetExtensions();
            IStorageAccountProvider storageAccountProvider = services.GetService<IStorageAccountProvider>();
            ILoggerFactory loggerFactory = services.GetService<ILoggerFactory>();
            IFunctionResultAggregatorFactory aggregatorFactory = services.GetService<IFunctionResultAggregatorFactory>();
            IAsyncCollector<FunctionInstanceLogEntry> functionEventCollector = null;

            // Create the aggregator if all the pieces are configured
            IAsyncCollector<FunctionInstanceLogEntry> aggregator = null;
            if (loggerFactory != null && aggregatorFactory != null && config.Aggregator.IsEnabled)
            {
                aggregator = aggregatorFactory.Create(config.Aggregator.BatchSize, config.Aggregator.FlushTimeout, loggerFactory);
            }

            IQueueConfiguration queueConfiguration = services.GetService<IQueueConfiguration>();
            var blobsConfiguration = config.Blobs;

            TraceWriter trace = services.GetService<TraceWriter>();
            IAsyncCollector<FunctionInstanceLogEntry> registeredFunctionEventCollector = services.GetService<IAsyncCollector<FunctionInstanceLogEntry>>();

            if (registeredFunctionEventCollector != null && aggregator != null)
            {
                // If there are both an aggregator and a registered FunctionEventCollector, wrap them in a composite
                functionEventCollector = new CompositeFunctionEventCollector(new[] { registeredFunctionEventCollector, aggregator });
            }
            else
            {
                // Otherwise, take whichever one is null (or use null if both are)
                functionEventCollector = aggregator ?? registeredFunctionEventCollector;
            }

            IWebJobsExceptionHandler exceptionHandler = services.GetService<IWebJobsExceptionHandler>();

            if (exceptionHandler != null)
            {
                exceptionHandler.Initialize(host);
            }

            bool hasFastTableHook = services.GetService<IAsyncCollector<FunctionInstanceLogEntry>>() != null;
            bool noDashboardStorage = config.DashboardConnectionString == null;

            // Only testing will override these interfaces. 
            IHostInstanceLoggerProvider hostInstanceLoggerProvider = services.GetService<IHostInstanceLoggerProvider>();
            IFunctionInstanceLoggerProvider functionInstanceLoggerProvider = services.GetService<IFunctionInstanceLoggerProvider>();
            IFunctionOutputLoggerProvider functionOutputLoggerProvider = services.GetService<IFunctionOutputLoggerProvider>();

            if (hostInstanceLoggerProvider == null && functionInstanceLoggerProvider == null && functionOutputLoggerProvider == null)
            {
                if (hasFastTableHook && noDashboardStorage)
                {
                    var loggerProvider = new FastTableLoggerProvider(trace);
                    hostInstanceLoggerProvider = loggerProvider;
                    functionInstanceLoggerProvider = loggerProvider;
                    functionOutputLoggerProvider = loggerProvider;
                }
                else
                {
                    var loggerProvider = new DefaultLoggerProvider(storageAccountProvider, trace);
                    hostInstanceLoggerProvider = loggerProvider;
                    functionInstanceLoggerProvider = loggerProvider;
                    functionOutputLoggerProvider = loggerProvider;
                }
            }

            using (CancellationTokenSource combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken))
            {
                CancellationToken combinedCancellationToken = combinedCancellationSource.Token;

                await WriteSiteExtensionManifestAsync(combinedCancellationToken);

                IStorageAccount dashboardAccount = await storageAccountProvider.GetDashboardAccountAsync(combinedCancellationToken);

                IHostInstanceLogger hostInstanceLogger = await hostInstanceLoggerProvider.GetAsync(combinedCancellationToken);
                IFunctionInstanceLogger functionInstanceLogger = await functionInstanceLoggerProvider.GetAsync(combinedCancellationToken);
                IFunctionOutputLogger functionOutputLogger = await functionOutputLoggerProvider.GetAsync(combinedCancellationToken);

                if (functionExecutor == null)
                {
                    functionExecutor = new FunctionExecutor(functionInstanceLogger, functionOutputLogger, exceptionHandler, trace, host, functionEventCollector, loggerFactory);
                    services.AddService(functionExecutor);
                }

                if (functionIndexProvider == null)
                {
                    functionIndexProvider = new FunctionIndexProvider(
                        services.GetService<ITypeLocator>(),
                        triggerBindingProvider,
                        bindingProvider,
                        activator,
                        functionExecutor,
                        extensions,
                        singletonManager,
                        trace,
                        loggerFactory);

                    // Important to set this so that the func we passed to DynamicHostIdProvider can pick it up. 
                    services.AddService<IFunctionIndexProvider>(functionIndexProvider);
                }

                IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);
                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll(), singletonManager, activator, nameResolver, trace, loggerFactory);

                IFunctionExecutor hostCallExecutor;
                IListener listener;
                HostOutputMessage hostOutputMessage;

                string hostId = await hostIdProvider.GetHostIdAsync(cancellationToken);
                if (string.Compare(config.HostId, hostId, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // if this isn't a static host ID, provide the HostId on the config
                    // so it is accessible
                    config.HostId = hostId;
                }

                if (dashboardAccount == null)
                {
                    hostCallExecutor = new ShutdownFunctionExecutor(shutdownToken, functionExecutor);

                    IListener factoryListener = new ListenerFactoryListener(functionsListenerFactory);
                    IListener shutdownListener = new ShutdownListener(shutdownToken, factoryListener);
                    listener = shutdownListener;

                    hostOutputMessage = new DataOnlyHostOutputMessage();
                }
                else
                {
                    string sharedQueueName = HostQueueNames.GetHostQueueName(hostId);
                    IStorageQueueClient dashboardQueueClient = dashboardAccount.CreateQueueClient();
                    IStorageQueue sharedQueue = dashboardQueueClient.GetQueueReference(sharedQueueName);
                    IListenerFactory sharedQueueListenerFactory = new HostMessageListenerFactory(sharedQueue,
                        queueConfiguration, exceptionHandler, trace, loggerFactory, functions,
                        functionInstanceLogger, functionExecutor);

                    Guid hostInstanceId = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(hostInstanceId.ToString("N"));
                    IStorageQueue instanceQueue = dashboardQueueClient.GetQueueReference(instanceQueueName);
                    IListenerFactory instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue,
                        queueConfiguration, exceptionHandler, trace, loggerFactory, functions,
                        functionInstanceLogger, functionExecutor);

                    HeartbeatDescriptor heartbeatDescriptor = new HeartbeatDescriptor
                    {
                        SharedContainerName = HostContainerNames.Hosts,
                        SharedDirectoryName = HostDirectoryNames.Heartbeats + "/" + hostId,
                        InstanceBlobName = hostInstanceId.ToString("N"),
                        ExpirationInSeconds = (int)HeartbeatIntervals.ExpirationInterval.TotalSeconds
                    };

                    IStorageBlockBlob blob = dashboardAccount.CreateBlobClient()
                        .GetContainerReference(heartbeatDescriptor.SharedContainerName)
                        .GetBlockBlobReference(heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName);
                    IRecurrentCommand heartbeatCommand = new UpdateHostHeartbeatCommand(new HeartbeatCommand(blob));

                    IEnumerable<MethodInfo> indexedMethods = functions.ReadAllMethods();
                    Assembly hostAssembly = GetHostAssembly(indexedMethods);
                    string displayName = hostAssembly != null ? AssemblyNameCache.GetName(hostAssembly).Name : "Unknown";

                    hostOutputMessage = new DataOnlyHostOutputMessage
                    {
                        HostInstanceId = hostInstanceId,
                        HostDisplayName = displayName,
                        SharedQueueName = sharedQueueName,
                        InstanceQueueName = instanceQueueName,
                        Heartbeat = heartbeatDescriptor,
                        WebJobRunIdentifier = WebJobRunIdentifier.Current
                    };

                    hostCallExecutor = CreateHostCallExecutor(instanceQueueListenerFactory, heartbeatCommand,
                        exceptionHandler, shutdownToken, functionExecutor);
                    IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
                    listener = CreateHostListener(hostListenerFactory, heartbeatCommand, exceptionHandler, shutdownToken);

                    // Publish this to Azure logging account so that a web dashboard can see it. 
                    await LogHostStartedAsync(functions, hostOutputMessage, hostInstanceLogger, combinedCancellationToken);
                }

                functionExecutor.HostOutputMessage = hostOutputMessage;

                IEnumerable<FunctionDescriptor> descriptors = functions.ReadAllDescriptors();
                int descriptorsCount = descriptors.Count();

                ILogger startupLogger = loggerFactory?.CreateLogger(LogCategories.Startup);

                if (config.UsingDevelopmentSettings)
                {
                    string msg = "Development settings applied";
                    trace.Verbose(msg);
                    startupLogger?.LogDebug(msg);
                }

                if (descriptorsCount == 0)
                {
                    string msg = string.Format("No job functions found. Try making your job classes and methods public. {0}",
                        Constants.ExtensionInitializationMessage);

                    trace.Warning(msg, Host.TraceSource.Indexing);
                    startupLogger?.LogWarning(msg);
                }
                else
                {
                    StringBuilder functionsTrace = new StringBuilder();
                    functionsTrace.AppendLine("Found the following functions:");

                    foreach (FunctionDescriptor descriptor in descriptors)
                    {
                        functionsTrace.AppendLine(descriptor.FullName);
                    }
                    string msg = functionsTrace.ToString();
                    trace.Info(msg, Host.TraceSource.Indexing);
                    startupLogger?.LogInformation(msg);
                }

                return new JobHostContext(
                    functions, 
                    hostCallExecutor, 
                    listener, 
                    trace,
                    functionEventCollector, 
                    loggerFactory);
            }
        }

        private static void InvokeExtensionConfigProviders(ExtensionConfigContext context)
        {
            IExtensionRegistry extensions = context.Config.GetExtensions();

            IEnumerable<IExtensionConfigProvider> configProviders = extensions.GetExtensions(typeof(IExtensionConfigProvider)).Cast<IExtensionConfigProvider>();
            foreach (IExtensionConfigProvider configProvider in configProviders)
            {
                context.Current = configProvider;
                configProvider.Initialize(context);
                context.ApplyRules();
            }
        }

        private static IFunctionExecutor CreateHostCallExecutor(IListenerFactory instanceQueueListenerFactory,
            IRecurrentCommand heartbeatCommand, IWebJobsExceptionHandler exceptionHandler,
            CancellationToken shutdownToken, IFunctionExecutor innerExecutor)
        {
            IFunctionExecutor heartbeatExecutor = new HeartbeatFunctionExecutor(heartbeatCommand,
                exceptionHandler, innerExecutor);
            IFunctionExecutor abortListenerExecutor = new AbortListenerFunctionExecutor(instanceQueueListenerFactory, heartbeatExecutor);
            IFunctionExecutor shutdownFunctionExecutor = new ShutdownFunctionExecutor(shutdownToken, abortListenerExecutor);
            return shutdownFunctionExecutor;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static IListener CreateHostListener(IListenerFactory allFunctionsListenerFactory,
            IRecurrentCommand heartbeatCommand, IWebJobsExceptionHandler exceptionHandler,
            CancellationToken shutdownToken)
        {
            IListener factoryListener = new ListenerFactoryListener(allFunctionsListenerFactory);
            IListener heartbeatListener = new HeartbeatListener(heartbeatCommand, exceptionHandler, factoryListener);
            IListener shutdownListener = new ShutdownListener(shutdownToken, heartbeatListener);
            return shutdownListener;
        }

        private static Assembly GetHostAssembly(IEnumerable<MethodInfo> methods)
        {
            // 1. Try to get the assembly name from the first method.
            MethodInfo firstMethod = methods.FirstOrDefault();

            if (firstMethod != null)
            {
                return firstMethod.DeclaringType.Assembly;
            }

            // 2. If there are no function definitions, try to use the entry assembly.
            Assembly entryAssembly = Assembly.GetEntryAssembly();

            if (entryAssembly != null)
            {
                return entryAssembly;
            }

            // 3. If there's no entry assembly either, we don't have anything to use.
            return null;
        }

        private static Task LogHostStartedAsync(IFunctionIndex functionIndex, HostOutputMessage hostOutputMessage,
            IHostInstanceLogger logger, CancellationToken cancellationToken)
        {
            IEnumerable<FunctionDescriptor> functions = functionIndex.ReadAllDescriptors();

            HostStartedMessage message = new HostStartedMessage
            {
                HostInstanceId = hostOutputMessage.HostInstanceId,
                HostDisplayName = hostOutputMessage.HostDisplayName,
                SharedQueueName = hostOutputMessage.SharedQueueName,
                InstanceQueueName = hostOutputMessage.InstanceQueueName,
                Heartbeat = hostOutputMessage.Heartbeat,
                WebJobRunIdentifier = hostOutputMessage.WebJobRunIdentifier,
                Functions = functions
            };

            return logger.LogHostStartedAsync(message, cancellationToken);
        }

        // When running in Azure Web Sites, write out a manifest file. This manifest file is read by
        // the Kudu site extension to provide custom behaviors for SDK jobs
        private static async Task WriteSiteExtensionManifestAsync(CancellationToken cancellationToken)
        {
            string jobDataPath = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.JobDataPath);
            if (jobDataPath == null)
            {
                // we're not in Azure Web Sites, bye bye.
                return;
            }

            const string Filename = "WebJobsSdk.marker";
            var path = Path.Combine(jobDataPath, Filename);
            const int DefaultBufferSize = 4096;

            try
            {
                using (Stream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, DefaultBufferSize, useAsync: true))
                using (TextWriter writer = new StreamWriter(stream))
                {
                    // content is not really important, this would help debugging though
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteAsync(DateTime.UtcNow.ToString("s") + "Z");
                    await writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException || ex is IOException)
                {
                    // simultaneous access error or an error caused by some other issue
                    // ignore it and skip marker creation
                }
                else
                {
                    throw;
                }
            }
        }

        private class DataOnlyHostOutputMessage : HostOutputMessage
        {
            internal override void AddMetadata(IDictionary<string, string> metadata)
            {
                throw new NotSupportedException();
            }
        }
    }
}
