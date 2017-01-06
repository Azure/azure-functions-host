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
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class JobHostContextFactory : IJobHostContextFactory
    {
        private readonly IStorageAccountProvider _storageAccountProvider;
        private readonly IConsoleProvider _consoleProvider;
        private readonly JobHostConfiguration _config;

        public JobHostContextFactory(IStorageAccountProvider storageAccountProvider, IConsoleProvider consoleProvider, JobHostConfiguration config)
        {
            _storageAccountProvider = storageAccountProvider;
            _consoleProvider = consoleProvider;
            _config = config;
        }

        public async Task<JobHostContext> CreateAndLogHostStartedAsync(JobHost host, CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            IHostIdProvider hostIdProvider = null;
            if (_config.HostId != null)
            {
                hostIdProvider = new FixedHostIdProvider(_config.HostId);
            }

            var fastLogger = _config.GetService<IAsyncCollector<FunctionInstanceLogEntry>>();
            IWebJobsExceptionHandler exceptionHandler = _config.GetService<IWebJobsExceptionHandler>();

            return await CreateAndLogHostStartedAsync(host, _storageAccountProvider, _config.Queues, _config.Blobs, _config.TypeLocator, _config.JobActivator,
                _config.NameResolver, _consoleProvider, _config, shutdownToken, cancellationToken, exceptionHandler, hostIdProvider, fastLogger: fastLogger);
        }

        public static async Task<JobHostContext> CreateAndLogHostStartedAsync(
            JobHost host,
            IStorageAccountProvider storageAccountProvider,
            IQueueConfiguration queueConfiguration,
            JobHostBlobsConfiguration blobsConfiguration,
            ITypeLocator typeLocator,
            IJobActivator activator,
            INameResolver nameResolver,
            IConsoleProvider consoleProvider,
            JobHostConfiguration config,
            CancellationToken shutdownToken,
            CancellationToken cancellationToken,
            IWebJobsExceptionHandler exceptionHandler,
            IHostIdProvider hostIdProvider = null,
            FunctionExecutor functionExecutor = null,
            IFunctionIndexProvider functionIndexProvider = null,
            IBindingProvider bindingProvider = null,
            IHostInstanceLoggerProvider hostInstanceLoggerProvider = null,
            IFunctionInstanceLoggerProvider functionInstanceLoggerProvider = null,
            IFunctionOutputLoggerProvider functionOutputLoggerProvider = null,
            SingletonManager singletonManager = null,
            IAsyncCollector<FunctionInstanceLogEntry> fastLogger = null)
        {
            if (hostIdProvider == null)
            {
                hostIdProvider = new DynamicHostIdProvider(storageAccountProvider, () => functionIndexProvider);
            }

            AzureStorageDeploymentValidator.Validate();

            var converterManager = config.ConverterManager;

            IExtensionTypeLocator extensionTypeLocator = new ExtensionTypeLocator(typeLocator);

            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor = new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor = new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();

            // Create a wrapper TraceWriter that delegates to both the user 
            // TraceWriters specified via config (if present), as well as to Console
            TraceWriter trace = new ConsoleTraceWriter(config.Tracing, consoleProvider.Out);

            // Register system services with the service container
            config.AddService<INameResolver>(nameResolver);

            if (exceptionHandler != null)
            {
                exceptionHandler.Initialize(host);
            }

            ExtensionConfigContext context = new ExtensionConfigContext
            {
                Config = config,
                Trace = trace                
            };
            InvokeExtensionConfigProviders(context);

            if (singletonManager == null)
            {
                singletonManager = new SingletonManager(storageAccountProvider, exceptionHandler, config.Singleton, trace, hostIdProvider, config.NameResolver);
            }

            IExtensionRegistry extensions = config.GetExtensions();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, extensionTypeLocator, hostIdProvider, queueConfiguration, blobsConfiguration, exceptionHandler,
                messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor, sharedContextProvider, extensions, singletonManager, trace);

            if (bindingProvider == null)
            {
                bindingProvider = DefaultBindingProvider.Create(nameResolver, converterManager, storageAccountProvider, extensionTypeLocator, messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor, extensions);
            }

            bool hasFastTableHook = config.GetService<IAsyncCollector<FunctionInstanceLogEntry>>() != null;
            bool noDashboardStorage = config.DashboardConnectionString == null;
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
                    functionExecutor = new FunctionExecutor(functionInstanceLogger, functionOutputLogger, exceptionHandler, trace, fastLogger);
                }

                if (functionIndexProvider == null)
                {
                    functionIndexProvider = new FunctionIndexProvider(typeLocator, triggerBindingProvider, bindingProvider, activator, functionExecutor, extensions, singletonManager, trace);
                }

                IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);
                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll(), singletonManager, activator, nameResolver, trace);

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
                        queueConfiguration, exceptionHandler, trace, functions,
                        functionInstanceLogger, functionExecutor);

                    Guid hostInstanceId = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(hostInstanceId.ToString("N"));
                    IStorageQueue instanceQueue = dashboardQueueClient.GetQueueReference(instanceQueueName);
                    IListenerFactory instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue,
                        queueConfiguration, exceptionHandler, trace, functions,
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
                    string displayName = hostAssembly != null ? hostAssembly.GetName().Name : "Unknown";

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

                if (config.UsingDevelopmentSettings)
                {
                    trace.Verbose(string.Format("Development settings applied"));
                }

                if (descriptorsCount == 0)
                {
                    trace.Warning(string.Format("No job functions found. Try making your job classes and methods public. {0}",
                        Constants.ExtensionInitializationMessage), TraceSource.Indexing);
                }
                else
                {
                    StringBuilder functionsTrace = new StringBuilder();
                    functionsTrace.AppendLine("Found the following functions:");

                    foreach (FunctionDescriptor descriptor in descriptors)
                    {
                        functionsTrace.AppendLine(descriptor.FullName);
                    }

                    trace.Info(functionsTrace.ToString(), TraceSource.Indexing);
                }

                return new JobHostContext(functions, hostCallExecutor, listener, trace, fastLogger);
            }
        }

        private static void InvokeExtensionConfigProviders(ExtensionConfigContext context)
        {
            IExtensionRegistry extensions = context.Config.GetExtensions();

            IEnumerable<IExtensionConfigProvider> configProviders = extensions.GetExtensions(typeof(IExtensionConfigProvider)).Cast<IExtensionConfigProvider>();
            foreach (IExtensionConfigProvider configProvider in configProviders)
            {
                configProvider.Initialize(context);
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
