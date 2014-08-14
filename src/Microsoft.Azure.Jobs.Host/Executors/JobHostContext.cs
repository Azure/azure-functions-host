// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Queues.Listeners;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal sealed class JobHostContext : IDisposable
    {
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionExecutor _executor;
        private readonly IListener _listener;

        private bool _disposed;

        public JobHostContext(IFunctionIndexLookup functionLookup,
            IFunctionExecutor executor,
            IListener listener)
        {
            _functionLookup = functionLookup;
            _executor = executor;
            _listener = listener;
        }

        public IFunctionIndexLookup FunctionLookup
        {
            get
            {
                ThrowIfDisposed();
                return _functionLookup;
            }
        }

        public IFunctionExecutor Executor
        {
            get
            {
                ThrowIfDisposed();
                return _executor;
            }
        }

        public IListener Listener
        {
            get
            {
                ThrowIfDisposed();
                return _listener;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                _listener.Dispose();

                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }

        public static async Task<JobHostContext> CreateAndLogHostStartedAsync(CloudStorageAccount dashboardAccount,
            CloudStorageAccount storageAccount, string serviceBusConnectionString,
            IStorageCredentialsValidator credentialsValidator, ITypeLocator typeLocator, INameResolver nameResolver,
            CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource combinedCancellationSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken))
            {
                CancellationToken combinedCancellationToken = combinedCancellationSource.Token;

                // This will make a network call to verify the credentials work.
                await credentialsValidator.ValidateCredentialsAsync(storageAccount, combinedCancellationToken);

                // Avoid double-validating the same credentials.
                if (storageAccount != null && storageAccount.Credentials != null && dashboardAccount != null &&
                    !storageAccount.Credentials.Equals(dashboardAccount.Credentials))
                {
                    // This will make a network call to verify the credentials work.
                    await credentialsValidator.ValidateCredentialsAsync(dashboardAccount, combinedCancellationToken);
                }

                CloudBlobClient blobClient;
                IHostInstanceLogger hostInstanceLogger;
                IFunctionInstanceLogger functionInstanceLogger;
                IFunctionOutputLogger functionOutputLogger;

                if (dashboardAccount != null)
                {
                    // Create logging against a live Azure account.
                    blobClient = dashboardAccount.CreateCloudBlobClient();
                    IPersistentQueueWriter<PersistentQueueMessage> queueWriter =
                        new PersistentQueueWriter<PersistentQueueMessage>(blobClient);
                    PersistentQueueLogger queueLogger = new PersistentQueueLogger(queueWriter);
                    hostInstanceLogger = queueLogger;
                    functionInstanceLogger = new CompositeFunctionInstanceLogger(
                        queueLogger,
                        new ConsoleFunctionInstanceLogger());
                    functionOutputLogger = new BlobFunctionOutputLogger(blobClient);
                }
                else
                {
                    // No auxillary logging. Logging interfaces are nops or in-memory.
                    blobClient = null;
                    hostInstanceLogger = new NullHostInstanceLogger();
                    functionInstanceLogger = new ConsoleFunctionInstanceLogger();
                    functionOutputLogger = new ConsoleFunctionOutputLogger();
                }

                FunctionIndexContext indexContext = new FunctionIndexContext(typeLocator, nameResolver, storageAccount,
                    serviceBusConnectionString, combinedCancellationToken);
                FunctionIndex functions = await FunctionIndex.CreateAsync(indexContext);
                HostBindingContext bindingContext = new HostBindingContext(functions.BindingProvider, nameResolver,
                    storageAccount, serviceBusConnectionString);

                IListenerFactory sharedQueueListenerFactory;
                IListenerFactory instanceQueueListenerFactory;
                IRecurrentCommand heartbeatCommand;
                HostOutputMessage hostOutputMessage;

                if (dashboardAccount != null)
                {
                    // Determine the host name from the method list
                    Assembly hostAssembly = GetHostAssembly(functions.ReadAllMethods());
                    string hostName = hostAssembly != null ? hostAssembly.FullName : "Unknown";
                    string sharedHostName = dashboardAccount.Credentials.AccountName + "/" + hostName;

                    IHostIdManager hostIdManager = new HostIdManager(blobClient);
                    Guid hostId = await hostIdManager.GetOrCreateHostIdAsync(sharedHostName, combinedCancellationToken);

                    string sharedQueueName = HostQueueNames.GetHostQueueName(hostId);
                    CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
                    CloudQueue sharedQueue = queueClient.GetQueueReference(sharedQueueName);
                    sharedQueueListenerFactory = new HostMessageListenerFactory(sharedQueue, functions,
                        functionInstanceLogger);

                    Guid id = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(id);
                    CloudQueue instanceQueue = queueClient.GetQueueReference(instanceQueueName);
                    instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue, functions,
                        functionInstanceLogger);

                    HeartbeatDescriptor heartbeatDescriptor = new HeartbeatDescriptor
                    {
                        SharedContainerName = HostContainerNames.Hosts,
                        SharedDirectoryName = HostDirectoryNames.Heartbeats + "/" + hostId.ToString("N"),
                        InstanceBlobName = id.ToString("N"),
                        ExpirationInSeconds = (int)HeartbeatIntervals.ExpirationInterval.TotalSeconds
                    };
                    heartbeatCommand = new UpdateHostHeartbeatCommand(new HeartbeatCommand(dashboardAccount,
                        heartbeatDescriptor.SharedContainerName,
                        heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName));

                    string displayName = hostAssembly != null ? hostAssembly.GetName().Name : "Unknown";

                    hostOutputMessage = new DataOnlyHostOutputMessage
                    {
                        HostInstanceId = id,
                        HostDisplayName = displayName,
                        SharedQueueName = sharedQueueName,
                        InstanceQueueName = instanceQueueName,
                        Heartbeat = heartbeatDescriptor,
                        WebJobRunIdentifier = WebJobRunIdentifier.Current
                    };

                    // Publish this to Azure logging account so that a web dashboard can see it. 
                    await LogHostStartedAsync(functions, hostOutputMessage, hostInstanceLogger,
                        combinedCancellationToken);
                }
                else
                {
                    sharedQueueListenerFactory = new NullListenerFactory();
                    instanceQueueListenerFactory = new NullListenerFactory();
                    heartbeatCommand = new NullRecurrentCommand();
                    hostOutputMessage = new DataOnlyHostOutputMessage();
                }

                IFunctionExecutor executor = new FunctionExecutor(new FunctionExecutorContext(functionInstanceLogger,
                    functionOutputLogger, bindingContext, hostOutputMessage));
                IListenerFactory allFunctionsListenerFactory = new HostListenerFactory(functions.ReadAll(),
                    sharedQueueListenerFactory, instanceQueueListenerFactory);

                IFunctionExecutor hostCallExecutor = CreateHostCallExecutor(instanceQueueListenerFactory,
                    bindingContext, heartbeatCommand, shutdownToken, executor);

                IListener listener = CreateHostListener(allFunctionsListenerFactory, bindingContext, heartbeatCommand,
                    shutdownToken, executor);

                return new JobHostContext(functions, hostCallExecutor, listener);
            }
        }

        private static IFunctionExecutor CreateHostCallExecutor(IListenerFactory instanceQueueListenerFactory,
            HostBindingContext bindingContext, IRecurrentCommand heartbeatCommand, CancellationToken shutdownToken,
            IFunctionExecutor innerExecutor)
        {
            IFunctionExecutor heartbeatExecutor = new HeartbeatFunctionExecutor(heartbeatCommand, innerExecutor);
            IFunctionExecutor abortListenerExecutor = new AbortListenerFunctionExecutor(instanceQueueListenerFactory,
                innerExecutor, bindingContext, heartbeatExecutor);
            IFunctionExecutor shutdownFunctionExecutor = new ShutdownFunctionExecutor(shutdownToken,
                abortListenerExecutor);
            return shutdownFunctionExecutor;
        }

        private static IListener CreateHostListener(IListenerFactory allFunctionsListenerFactory,
            HostBindingContext bindingContext, IRecurrentCommand heartbeatCommand, CancellationToken shutdownToken,
            IFunctionExecutor executor)
        {
            IListener factoryListener = new ListenerFactoryListener(allFunctionsListenerFactory, executor,
                bindingContext);
            IListener heartbeatListener = new HeartbeatListener(heartbeatCommand, factoryListener);
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

        private class DataOnlyHostOutputMessage : HostOutputMessage
        {
            internal override void AddMetadata(IDictionary<string, string> metadata)
            {
                throw new NotSupportedException();
            }
        }
    }
}
