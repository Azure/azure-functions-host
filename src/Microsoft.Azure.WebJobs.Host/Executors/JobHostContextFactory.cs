// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
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
        private readonly IServiceBusAccountProvider _serviceBusAccountProvider;
        private readonly ITypeLocator _typeLocator;
        private readonly INameResolver _nameResolver;
        private readonly IJobActivator _activator;
        private readonly string _hostId;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IConsoleProvider _consoleProvider;

        public JobHostContextFactory(IStorageAccountProvider storageAccountProvider, IServiceBusAccountProvider
            serviceBusAccountProvider, ITypeLocator typeLocator, INameResolver nameResolver, IJobActivator activator,
            string hostId, IQueueConfiguration queueConfiguration, IConsoleProvider consoleProvider)
        {
            _storageAccountProvider = storageAccountProvider;
            _serviceBusAccountProvider = serviceBusAccountProvider;
            _typeLocator = typeLocator;
            _activator = activator;
            _nameResolver = nameResolver;
            _hostId = hostId;
            _queueConfiguration = queueConfiguration;
            _consoleProvider = consoleProvider;
        }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken shutdownToken,
            CancellationToken cancellationToken)
        {
            IFunctionIndexProvider functionIndexProvider = null;
            IHostIdProvider hostIdProvider = _hostId != null ? (IHostIdProvider)new FixedHostIdProvider(_hostId)
                : new DynamicHostIdProvider(_storageAccountProvider, () => functionIndexProvider);
            IExtensionTypeLocator extensionTypeLocator = new ExtensionTypeLocator(_typeLocator);
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher = BackgroundExceptionDispatcher.Instance;
            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor =
                new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor =
                new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(_nameResolver,
                _storageAccountProvider, _serviceBusAccountProvider, extensionTypeLocator, hostIdProvider,
                _queueConfiguration, backgroundExceptionDispatcher, messageEnqueuedWatcherAccessor,
                blobWrittenWatcherAccessor, sharedContextProvider, _consoleProvider.Out);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(_nameResolver, _storageAccountProvider,
                _serviceBusAccountProvider, extensionTypeLocator, messageEnqueuedWatcherAccessor,
                blobWrittenWatcherAccessor);
            functionIndexProvider = new FunctionIndexProvider(_typeLocator, triggerBindingProvider, bindingProvider,
                _activator);
            DefaultLoggerProvider loggerProvider = new DefaultLoggerProvider(_storageAccountProvider);
            return CreateAndLogHostStartedAsync(_storageAccountProvider, functionIndexProvider, bindingProvider,
                hostIdProvider, loggerProvider, loggerProvider, loggerProvider, _queueConfiguration,
                backgroundExceptionDispatcher, _consoleProvider,
                shutdownToken, cancellationToken);
        }

        internal static async Task<JobHostContext> CreateAndLogHostStartedAsync(
            IStorageAccountProvider storageAccountProvider,
            IFunctionIndexProvider functionIndexProvider,
            IBindingProvider bindingProvider,
            IHostIdProvider hostIdProvider,
            IHostInstanceLoggerProvider hostInstanceLoggerProvider,
            IFunctionInstanceLoggerProvider functionInstanceLoggerProvider,
            IFunctionOutputLoggerProvider functionOutputLoggerProvider,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IConsoleProvider consoleProvider,
            CancellationToken shutdownToken,
            CancellationToken cancellationToken)
        {
            using (CancellationTokenSource combinedCancellationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken))
            {
                CancellationToken combinedCancellationToken = combinedCancellationSource.Token;

                await WriteSiteExtensionManifestAsync(combinedCancellationToken);

                IStorageAccount dashboardAccount = await storageAccountProvider.GetDashboardAccountAsync(
                    combinedCancellationToken);

                IHostInstanceLogger hostInstanceLogger = await hostInstanceLoggerProvider.GetAsync(
                    combinedCancellationToken);
                IFunctionInstanceLogger functionInstanceLogger = await functionInstanceLoggerProvider.GetAsync(
                    combinedCancellationToken);
                IFunctionOutputLogger functionOutputLogger = await functionOutputLoggerProvider.GetAsync(
                    combinedCancellationToken);

                IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);

                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll());

                FunctionExecutor executor = new FunctionExecutor(functionInstanceLogger, functionOutputLogger,
                    backgroundExceptionDispatcher);

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
                        functionInstanceLogger);

                    Guid hostInstanceId = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(hostInstanceId.ToString("N"));
                    IStorageQueue instanceQueue = dashboardQueueClient.GetQueueReference(instanceQueueName);
                    IListenerFactory instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue,
                        queueConfiguration, backgroundExceptionDispatcher, consoleOut, functions,
                        functionInstanceLogger);

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
                        backgroundExceptionDispatcher, shutdownToken, executor);
                    IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
                    listener = CreateHostListener(hostListenerFactory, heartbeatCommand, backgroundExceptionDispatcher,
                        shutdownToken, executor);

                    // Publish this to Azure logging account so that a web dashboard can see it. 
                    await LogHostStartedAsync(functions, hostOutputMessage, hostInstanceLogger,
                        combinedCancellationToken);
                }
                else
                {
                    hostCallExecutor = new ShutdownFunctionExecutor(shutdownToken, executor);

                    IListener factoryListener = new ListenerFactoryListener(functionsListenerFactory, executor);
                    IListener shutdownListener = new ShutdownListener(shutdownToken, factoryListener);
                    listener = shutdownListener;

                    hostOutputMessage = new DataOnlyHostOutputMessage();
                }

                executor.HostOutputMessage = hostOutputMessage;

                IEnumerable<FunctionDescriptor> descriptors = functions.ReadAllDescriptors();
                int descriptorsCount = descriptors.Count();

                if (descriptorsCount == 0)
                {
                    consoleOut.WriteLine(
                        "No functions found. Try making job classes and methods public.");
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
            CancellationToken shutdownToken, IFunctionExecutor executor)
        {
            IListener factoryListener = new ListenerFactoryListener(allFunctionsListenerFactory, executor);
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

        // When running in Azure Web Sites, write out a manifest file.
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
                using (Stream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
                        defaultBufferSize, useAsync: true))
                using (TextWriter writer = new StreamWriter(stream))
                {
                    // content is not really important, this would help debugging though
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteAsync(DateTime.UtcNow.ToString("s") + "Z");
                    await writer.FlushAsync();
                }
            }
            catch (UnauthorizedAccessException)
            {
                // simultaneous access error or an error caused by some other issue
                // ignore it and skip marker creation
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
