// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public static class JobHostFactory
    {
        public static TestJobHost<TProgram> Create<TProgram>()
        {
            return Create<TProgram>(CloudStorageAccount.DevelopmentStorageAccount, maxDequeueCount: 5);
        }

        public static TestJobHost<TProgram> Create<TProgram>(int maxDequeueCount)
        {
            return Create<TProgram>(CloudStorageAccount.DevelopmentStorageAccount, maxDequeueCount);
        }

        public static TestJobHost<TProgram> Create<TProgram>(CloudStorageAccount storageAccount)
        {
            return Create<TProgram>(storageAccount, maxDequeueCount: 5);
        }

        public static TestJobHost<TProgram> Create<TProgram>(CloudStorageAccount storageAccount, int maxDequeueCount)
        {
            TestJobHostConfiguration config = new TestJobHostConfiguration();

            IStorageAccountProvider storageAccountProvider = new SimpleStorageAccountProvider(config)
            {
                StorageAccount = storageAccount,
                // use null logging string since unit tests don't need logs.
                DashboardAccount = null
            };

            IExtensionTypeLocator extensionTypeLocator = new NullExtensionTypeLocator();
            IHostIdProvider hostIdProvider = new FixedHostIdProvider("test");
            INameResolver nameResolver = null;
            IQueueConfiguration queueConfiguration = new SimpleQueueConfiguration(maxDequeueCount);
            JobHostBlobsConfiguration blobsConfiguration = new JobHostBlobsConfiguration();
            ContextAccessor<IMessageEnqueuedWatcher> messageEnqueuedWatcherAccessor =
                new ContextAccessor<IMessageEnqueuedWatcher>();
            ContextAccessor<IBlobWrittenWatcher> blobWrittenWatcherAccessor =
                new ContextAccessor<IBlobWrittenWatcher>();
            ISharedContextProvider sharedContextProvider = new SharedContextProvider();
            IExtensionRegistry extensions = new DefaultExtensionRegistry();

            SingletonManager singletonManager = new SingletonManager();

            TraceWriter trace = new TestTraceWriter(TraceLevel.Verbose);
            IWebJobsExceptionHandler exceptionHandler = new WebJobsExceptionHandler();
            IFunctionOutputLoggerProvider outputLoggerProvider = new NullFunctionOutputLoggerProvider();
            IFunctionOutputLogger outputLogger = outputLoggerProvider.GetAsync(CancellationToken.None).Result;

            IFunctionExecutor executor = new FunctionExecutor(new NullFunctionInstanceLogger(), outputLogger, exceptionHandler, trace);

            var triggerBindingProvider = DefaultTriggerBindingProvider.Create(
                    nameResolver, storageAccountProvider, extensionTypeLocator, hostIdProvider, queueConfiguration, blobsConfiguration,
                    exceptionHandler, messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor,
                    sharedContextProvider, extensions, singletonManager, new TestTraceWriter(TraceLevel.Verbose));

            var bindingProvider = DefaultBindingProvider.Create(nameResolver, storageAccountProvider, extensionTypeLocator,
                        messageEnqueuedWatcherAccessor, blobWrittenWatcherAccessor, extensions);

            var functionIndexProvider = new FunctionIndexProvider(new FakeTypeLocator(typeof(TProgram)), triggerBindingProvider, bindingProvider, DefaultJobActivator.Instance, executor, new DefaultExtensionRegistry(), singletonManager, trace);

            IJobHostContextFactory contextFactory = new TestJobHostContextFactory
            {
                FunctionIndexProvider = functionIndexProvider,
                StorageAccountProvider = storageAccountProvider,
                Queues = queueConfiguration,
                SingletonManager = singletonManager,
                HostIdProvider = hostIdProvider
            };

            config.ContextFactory = contextFactory;

            return new TestJobHost<TProgram>(config);
        }
    }
}
