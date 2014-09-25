// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class JobHostContextFactory
    {
        private readonly IStorageAccount _dashboardAccount;
        private readonly IStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;
        private readonly IStorageCredentialsValidator _credentialsValidator;
        private readonly ITypeLocator _typeLocator;
        private readonly INameResolver _nameResolver;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IHostInstanceLogger _hostInstanceLogger;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly CancellationToken _shutdownToken;

        public JobHostContextFactory(IStorageAccount dashboardAccount,
            IStorageAccount storageAccount,
            string serviceBusConnectionString,
            IStorageCredentialsValidator credentialsValidator,
            ITypeLocator typeLocator,
            INameResolver nameResolver,
            IHostIdProvider hostIdProvider,
            IHostInstanceLogger hostInstanceLogger,
            IFunctionInstanceLogger functionInstanceLogger,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            CancellationToken shutdownToken)
        {
            _dashboardAccount = dashboardAccount;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
            _credentialsValidator = credentialsValidator;
            _typeLocator = typeLocator;
            _nameResolver = nameResolver;
            _hostIdProvider = hostIdProvider;
            _hostInstanceLogger = hostInstanceLogger;
            _functionInstanceLogger = functionInstanceLogger;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _shutdownToken = shutdownToken;
        }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken cancellationToken)
        {
            return JobHostContext.CreateAndLogHostStartedAsync(_dashboardAccount, _storageAccount,
                _serviceBusConnectionString, _credentialsValidator, _typeLocator, _nameResolver, _hostIdProvider,
                _hostInstanceLogger, _functionInstanceLogger, _queueConfiguration, _backgroundExceptionDispatcher,
                _shutdownToken, cancellationToken);
        }
    }
}
