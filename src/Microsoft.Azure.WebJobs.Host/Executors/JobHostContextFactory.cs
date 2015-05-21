// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        public async Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            IHostIdProvider hostIdProvider = null;
            if (_config.HostId != null)
            {
                hostIdProvider = new FixedHostIdProvider(_config.HostId);
            }
            return await CreateAndLogHostStartedAsync(_storageAccountProvider, _config.Queues, _config.TypeLocator, _config.JobActivator, 
                _config.NameResolver, _consoleProvider, _config, shutdownToken, cancellationToken, hostIdProvider);
        }

        public static async Task<JobHostContext> CreateAndLogHostStartedAsync(
            IStorageAccountProvider storageAccountProvider,
            IQueueConfiguration queueConfiguration,
            ITypeLocator typeLocator,
            IJobActivator activator,
            INameResolver nameResolver,
            IConsoleProvider consoleProvider,
            JobHostConfiguration config,
            CancellationToken shutdownToken, 
            CancellationToken cancellationToken,
            IHostIdProvider hostIdProvider = null,
            FunctionExecutor functionExecutor = null,
            IFunctionIndexProvider functionIndexProvider = null,
            IBindingProvider bindingProvider = null,
            IHostInstanceLoggerProvider hostInstanceLogerProvider = null,
            IFunctionInstanceLoggerProvider functionInstanceLoggerProvider = null,
            IFunctionOutputLoggerProvider functionOutputLoggerProvider = null,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher = null)
        {
            if (hostIdProvider == null)
            {
                hostIdProvider = new DynamicHostIdProvider(storageAccountProvider, () => functionIndexProvider);
            }

            IExtensionTypeLocator extensionTypeLocator = new ExtensionTypeLocator(typeLocator);
            if (backgroundExceptionDispatcher == null)
            {
                backgroundExceptionDispatcher = BackgroundExceptionDispatcher.Instance;
            }
            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor = new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor = new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();

            // Register system services with the service container
            config.AddService<INameResolver>(nameResolver);

            ExtensionConfigContext context = new ExtensionConfigContext
            {
                Config = config
            };
            InvokeExtensionConfigProviders(context);

            IExtensionRegistry extensions = config.GetExtensions();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, extensionTypeLocator, hostIdProvider, queueConfiguration, backgroundExceptionDispatcher,
                messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor, sharedContextProvider, extensions, consoleProvider.Out);

            if (bindingProvider == null)
            {
                bindingProvider = DefaultBindingProvider.Create(nameResolver, storageAccountProvider, extensionTypeLocator, messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor, extensions);
            }

            DefaultLoggerProvider loggerProvider = new DefaultLoggerProvider(storageAccountProvider);

            using (CancellationTokenSource combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken))
            {
                CancellationToken combinedCancellationToken = combinedCancellationSource.Token;

                await WriteSiteExtensionManifestAsync(combinedCancellationToken);

                IStorageAccount dashboardAccount = await storageAccountProvider.GetDashboardAccountAsync(combinedCancellationToken);

                IHostInstanceLogger hostInstanceLogger = null;
                if (hostInstanceLogerProvider != null)
                {
                    hostInstanceLogger = await hostInstanceLogerProvider.GetAsync(combinedCancellationToken);
                }
                else
                {
                    hostInstanceLogger = await ((IHostInstanceLoggerProvider)loggerProvider).GetAsync(combinedCancellationToken);
                }

                IFunctionInstanceLogger functionInstanceLogger = null;
                if (functionInstanceLoggerProvider != null)
                {
                    functionInstanceLogger = await functionInstanceLoggerProvider.GetAsync(combinedCancellationToken);
                }
                else
                {
                    functionInstanceLogger = (IFunctionInstanceLogger)(await ((IFunctionInstanceLoggerProvider)loggerProvider).GetAsync(combinedCancellationToken));
                }

                IFunctionOutputLogger functionOutputLogger = null;
                if (functionOutputLoggerProvider != null)
                {
                    functionOutputLogger = await functionOutputLoggerProvider.GetAsync(combinedCancellationToken);
                }
                else
                {
                    functionOutputLogger = (IFunctionOutputLogger)(await ((IFunctionOutputLoggerProvider)loggerProvider).GetAsync(combinedCancellationToken));
                }
   
                if (functionExecutor == null)
                {
                    functionExecutor = new FunctionExecutor(functionInstanceLogger, functionOutputLogger, backgroundExceptionDispatcher);
                }

                if (functionIndexProvider == null)
                {
                    functionIndexProvider = new FunctionIndexProvider(typeLocator, triggerBindingProvider, bindingProvider, activator, functionExecutor, extensions);
                }

                IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);
                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll());

                TextWriter consoleOut = consoleProvider.Out;
                IFunctionExecutor hostCallExecutor;
                IListener listener;
                HostOutputMessage hostOutputMessage;

                if (dashboardAccount != null)
                {
                    string hostId = await hostIdProvider.GetHostIdAsync(cancellationToken);

                    string sharedQueueName = HostQueueNames.GetHostQueueName(hostId);
                    IStorageQueueClient dashboardQueueClient = dashboardAccount.CreateQueueClient();
                    IStorageQueue sharedQueue = dashboardQueueClient.GetQueueReference(sharedQueueName);
                    IListenerFactory sharedQueueListenerFactory = new HostMessageListenerFactory(sharedQueue,
                        queueConfiguration, backgroundExceptionDispatcher, consoleOut, functions,
                        functionInstanceLogger, functionExecutor);

                    Guid hostInstanceId = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(hostInstanceId.ToString("N"));
                    IStorageQueue instanceQueue = dashboardQueueClient.GetQueueReference(instanceQueueName);
                    IListenerFactory instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue,
                        queueConfiguration, backgroundExceptionDispatcher, consoleOut, functions,
                        functionInstanceLogger, functionExecutor);

                    HeartbeatDescriptor heartbeatDescriptor = new HeartbeatDescriptor
                    {
                        SharedContainerName = HostContainerNames.Hosts,
                        SharedDirectoryName = HostDirectoryNames.Heartbeats + "/" + hostId,
                        InstanceBlobName = hostInstanceId.ToString("N"),
                        ExpirationInSeconds = (int)HeartbeatIntervals.ExpirationInterval.TotalSeconds
                    };
                    IRecurrentCommand heartbeatCommand = new UpdateHostHeartbeatCommand(new HeartbeatCommand(
                        dashboardAccount,
                        heartbeatDescriptor.SharedContainerName,
                        heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName));

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
                        backgroundExceptionDispatcher, shutdownToken, functionExecutor);
                    IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
                    listener = CreateHostListener(hostListenerFactory, heartbeatCommand, backgroundExceptionDispatcher, shutdownToken);

                    // Publish this to Azure logging account so that a web dashboard can see it. 
                    await LogHostStartedAsync(functions, hostOutputMessage, hostInstanceLogger, combinedCancellationToken);
                }
                else
                {
                    hostCallExecutor = new ShutdownFunctionExecutor(shutdownToken, functionExecutor);

                    IListener factoryListener = new ListenerFactoryListener(functionsListenerFactory);
                    IListener shutdownListener = new ShutdownListener(shutdownToken, factoryListener);
                    listener = shutdownListener;

                    hostOutputMessage = new DataOnlyHostOutputMessage();
                }

                functionExecutor.HostOutputMessage = hostOutputMessage;

                IEnumerable<FunctionDescriptor> descriptors = functions.ReadAllDescriptors();
                int descriptorsCount = descriptors.Count();

                if (descriptorsCount == 0)
                {
                    consoleOut.WriteLine("No functions found. Try making job classes and methods public.");
                }
                else
                {
                    consoleOut.WriteLine("Found the following functions:");

                    foreach (FunctionDescriptor descriptor in descriptors)
                    {
                        consoleOut.WriteLine(descriptor.FullName);
                    }
                }

                return new JobHostContext(functions, hostCallExecutor, listener, consoleOut);
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
            IRecurrentCommand heartbeatCommand, IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            CancellationToken shutdownToken, IFunctionExecutor innerExecutor)
        {
            IFunctionExecutor heartbeatExecutor = new HeartbeatFunctionExecutor(heartbeatCommand,
                backgroundExceptionDispatcher, innerExecutor);
            IFunctionExecutor abortListenerExecutor = new AbortListenerFunctionExecutor(instanceQueueListenerFactory,
                innerExecutor, heartbeatExecutor);
            IFunctionExecutor shutdownFunctionExecutor = new ShutdownFunctionExecutor(shutdownToken,
                abortListenerExecutor);
            return shutdownFunctionExecutor;
        }

        private static IListener CreateHostListener(IListenerFactory allFunctionsListenerFactory,
            IRecurrentCommand heartbeatCommand, IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            CancellationToken shutdownToken)
        {
            IListener factoryListener = new ListenerFactoryListener(allFunctionsListenerFactory);
            IListener heartbeatListener = new HeartbeatListener(heartbeatCommand,
                backgroundExceptionDispatcher, factoryListener);
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

            const string filename = "WebJobsSdk.marker";
            var path = Path.Combine(jobDataPath, filename);
            const int defaultBufferSize = 4096;

            try
            {
                using (Stream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, defaultBufferSize, useAsync: true))
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
