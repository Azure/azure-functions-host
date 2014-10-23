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
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class JobHostContextFactory
    {
        private readonly IStorageAccountProvider _storageAccountProvider;
        private readonly IFunctionIndexProvider _functionIndexProvider;
        private readonly IBindingProvider _bindingProvider;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IHostInstanceLoggerProvider _hostInstanceLoggerProvider;
        private readonly IFunctionInstanceLoggerProvider _functionInstanceLoggerProvider;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly CancellationToken _shutdownToken;

        public JobHostContextFactory(IStorageAccountProvider storageAccountProvider,
            IFunctionIndexProvider functionIndexProvider,
            IBindingProvider bindingProvider,
            IHostIdProvider hostIdProvider,
            IHostInstanceLoggerProvider hostInstanceLoggerProvider,
            IFunctionInstanceLoggerProvider functionInstanceLoggerProvider,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            CancellationToken shutdownToken)
        {
            _storageAccountProvider = storageAccountProvider;
            _functionIndexProvider = functionIndexProvider;
            _bindingProvider = bindingProvider;
            _hostIdProvider = hostIdProvider;
            _hostInstanceLoggerProvider = hostInstanceLoggerProvider;
            _functionInstanceLoggerProvider = functionInstanceLoggerProvider;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _shutdownToken = shutdownToken;
        }

        public async Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken cancellationToken)
        {
            using (CancellationTokenSource combinedCancellationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownToken))
            {
                CancellationToken combinedCancellationToken = combinedCancellationSource.Token;

                await WriteSiteExtensionManifestAsync(combinedCancellationToken);

                IStorageAccount dashboardAccount = await _storageAccountProvider.GetDashboardAccountAsync(
                    combinedCancellationToken);
                CloudStorageAccount sdkDashboardAccount = dashboardAccount != null ? dashboardAccount.SdkObject : null;

                IHostInstanceLogger hostInstanceLogger = await _hostInstanceLoggerProvider.GetAsync(
                    combinedCancellationToken);
                IFunctionInstanceLogger functionInstanceLogger = await _functionInstanceLoggerProvider.GetAsync(
                    combinedCancellationToken);

                IFunctionOutputLogger functionOutputLogger;

                if (dashboardAccount != null)
                {
                    CloudBlobClient dashboardBlobClient = sdkDashboardAccount.CreateCloudBlobClient();
                    functionOutputLogger = new BlobFunctionOutputLogger(dashboardBlobClient);
                }
                else
                {
                    functionOutputLogger = new ConsoleFunctionOutputLogger();
                }

                IFunctionIndex functions = await _functionIndexProvider.GetAsync(combinedCancellationToken);

                FunctionExecutorContext executorContext = new FunctionExecutorContext();
                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll());

                IFunctionExecutor executor = new FunctionExecutor(functionInstanceLogger,
                    functionOutputLogger, _backgroundExceptionDispatcher, executorContext);

                IFunctionExecutor hostCallExecutor;
                IListener listener;
                HostOutputMessage hostOutputMessage;

                if (dashboardAccount != null)
                {
                    string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);

                    string sharedQueueName = HostQueueNames.GetHostQueueName(hostId);
                    IStorageQueueClient dashboardQueueClient = dashboardAccount.CreateQueueClient();
                    IStorageQueue sharedQueue = dashboardQueueClient.GetQueueReference(sharedQueueName);
                    IListenerFactory sharedQueueListenerFactory = new HostMessageListenerFactory(sharedQueue,
                        _queueConfiguration, _backgroundExceptionDispatcher, functions, functionInstanceLogger);

                    Guid hostInstanceId = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(hostInstanceId.ToString("N"));
                    IStorageQueue instanceQueue = dashboardQueueClient.GetQueueReference(instanceQueueName);
                    IListenerFactory instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue,
                        _queueConfiguration, _backgroundExceptionDispatcher, functions, functionInstanceLogger);

                    HeartbeatDescriptor heartbeatDescriptor = new HeartbeatDescriptor
                    {
                        SharedContainerName = HostContainerNames.Hosts,
                        SharedDirectoryName = HostDirectoryNames.Heartbeats + "/" + hostId,
                        InstanceBlobName = hostInstanceId.ToString("N"),
                        ExpirationInSeconds = (int)HeartbeatIntervals.ExpirationInterval.TotalSeconds
                    };
                    IRecurrentCommand heartbeatCommand = new UpdateHostHeartbeatCommand(new HeartbeatCommand(
                        sdkDashboardAccount,
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
                        _shutdownToken, executor);
                    IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
                    listener = CreateHostListener(hostListenerFactory, heartbeatCommand, _shutdownToken, executor);

                    // Publish this to Azure logging account so that a web dashboard can see it. 
                    await LogHostStartedAsync(functions, hostOutputMessage, hostInstanceLogger,
                        combinedCancellationToken);
                }
                else
                {
                    hostCallExecutor = new ShutdownFunctionExecutor(_shutdownToken, executor);

                    IListener factoryListener = new ListenerFactoryListener(functionsListenerFactory, executor);
                    IListener shutdownListener = new ShutdownListener(_shutdownToken, factoryListener);
                    listener = shutdownListener;

                    hostOutputMessage = new DataOnlyHostOutputMessage();
                }

                executorContext.HostOutputMessage = hostOutputMessage;

                IEnumerable<FunctionDescriptor> descriptors = functions.ReadAllDescriptors();
                int descriptorsCount = descriptors.Count();

                if (descriptorsCount == 0)
                {
                    Console.WriteLine(
                        "No functions found. Try making job classes public and methods public static.");
                }
                else
                {
                    Console.WriteLine("Found the following functions:");

                    foreach (FunctionDescriptor descriptor in descriptors)
                    {
                        Console.WriteLine(descriptor.FullName);
                    }
                }

                return new JobHostContext(functions, hostCallExecutor, listener);
            }
        }

        private IFunctionExecutor CreateHostCallExecutor(IListenerFactory instanceQueueListenerFactory,
            IRecurrentCommand heartbeatCommand, CancellationToken shutdownToken,
            IFunctionExecutor innerExecutor)
        {
            IFunctionExecutor heartbeatExecutor = new HeartbeatFunctionExecutor(heartbeatCommand,
                _backgroundExceptionDispatcher, innerExecutor);
            IFunctionExecutor abortListenerExecutor = new AbortListenerFunctionExecutor(instanceQueueListenerFactory,
                innerExecutor, heartbeatExecutor);
            IFunctionExecutor shutdownFunctionExecutor = new ShutdownFunctionExecutor(shutdownToken,
                abortListenerExecutor);
            return shutdownFunctionExecutor;
        }

        private IListener CreateHostListener(IListenerFactory allFunctionsListenerFactory,
            IRecurrentCommand heartbeatCommand, CancellationToken shutdownToken, IFunctionExecutor executor)
        {
            IListener factoryListener = new ListenerFactoryListener(allFunctionsListenerFactory, executor);
            IListener heartbeatListener = new HeartbeatListener(heartbeatCommand,
                _backgroundExceptionDispatcher, factoryListener);
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

        private class DataOnlyHostOutputMessage : HostOutputMessage
        {
            internal override void AddMetadata(IDictionary<string, string> metadata)
            {
                throw new NotSupportedException();
            }
        }
    }
}
