// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Moq;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal static class FunctionIndexerFactory
    {
        public static FunctionIndexer Create(CloudStorageAccount account = null, INameResolver nameResolver = null, IExtensionRegistry extensionRegistry = null)
        {
            Mock<IServiceProvider> services = new Mock<IServiceProvider>(MockBehavior.Strict);
            StorageClientFactory clientFactory = new StorageClientFactory();
            services.Setup(p => p.GetService(typeof(StorageClientFactory))).Returns(clientFactory);
            IStorageAccount storageAccount = account != null ? new StorageAccount(account, services.Object) : null;
            IStorageAccountProvider storageAccountProvider = new SimpleStorageAccountProvider(services.Object)
            {
                StorageAccount = account
            };
            IExtensionTypeLocator extensionTypeLocator = new NullExtensionTypeLocator();
            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor =
                new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor =
                new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();
            TestTraceWriter logger = new TestTraceWriter(TraceLevel.Verbose);
            ITriggerBindingProvider triggerBindingProvider = DefaultTriggerBindingProvider.Create(nameResolver,
                storageAccountProvider, extensionTypeLocator,
                new FixedHostIdProvider("test"), new SimpleQueueConfiguration(maxDequeueCount: 5),
                BackgroundExceptionDispatcher.Instance, messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor,
                sharedContextProvider, new DefaultExtensionRegistry(), logger);
            IBindingProvider bindingProvider = DefaultBindingProvider.Create(nameResolver, storageAccountProvider,
                extensionTypeLocator, messageEnqueuedWatcherAccessor,
                blobWrittenWatcherAccessor, new DefaultExtensionRegistry());

            IFunctionOutputLoggerProvider outputLoggerProvider = new NullFunctionOutputLoggerProvider();
            IFunctionOutputLogger outputLogger = outputLoggerProvider.GetAsync(CancellationToken.None).Result;
            IFunctionExecutor executor = new FunctionExecutor(new NullFunctionInstanceLogger(), outputLogger, BackgroundExceptionDispatcher.Instance, new TestTraceWriter(TraceLevel.Verbose), null);

            if (extensionRegistry == null)
            {
                extensionRegistry = new DefaultExtensionRegistry();
            }

            return new FunctionIndexer(triggerBindingProvider, bindingProvider, DefaultJobActivator.Instance, executor, extensionRegistry, new SingletonManager());
        }
    }
}
