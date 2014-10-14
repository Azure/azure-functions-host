// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class JobHostContextFactory
    {
        private readonly IStorageAccountProvider _storageAccountProvider;
        private readonly IServiceBusAccountProvider _serviceBusAccountProvider;
        private readonly IFunctionIndexProvider _functionIndexProvider;
        private readonly INameResolver _nameResolver;
        private readonly IBindingProvider _bindingProvider;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IHostInstanceLoggerProvider _hostInstanceLoggerProvider;
        private readonly IFunctionInstanceLoggerProvider _functionInstanceLoggerProvider;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly CancellationToken _shutdownToken;

        public JobHostContextFactory(IStorageAccountProvider storageAccountProvider,
            IServiceBusAccountProvider serviceBusAccountProvider,
            IFunctionIndexProvider functionIndexProvider,
            INameResolver nameResolver,
            IBindingProvider bindingProvider,
            IHostIdProvider hostIdProvider,
            IHostInstanceLoggerProvider hostInstanceLoggerProvider,
            IFunctionInstanceLoggerProvider functionInstanceLoggerProvider,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            CancellationToken shutdownToken)
        {
            _storageAccountProvider = storageAccountProvider;
            _serviceBusAccountProvider = serviceBusAccountProvider;
            _functionIndexProvider = functionIndexProvider;
            _nameResolver = nameResolver;
            _bindingProvider = bindingProvider;
            _hostIdProvider = hostIdProvider;
            _hostInstanceLoggerProvider = hostInstanceLoggerProvider;
            _functionInstanceLoggerProvider = functionInstanceLoggerProvider;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _shutdownToken = shutdownToken;
        }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken cancellationToken)
        {
            return JobHostContext.CreateAndLogHostStartedAsync(_storageAccountProvider, _serviceBusAccountProvider,
                _functionIndexProvider, _nameResolver, _bindingProvider, _hostIdProvider, _hostInstanceLoggerProvider,
                _functionInstanceLoggerProvider, _queueConfiguration, _backgroundExceptionDispatcher, _shutdownToken,
                cancellationToken);
        }
    }
}
