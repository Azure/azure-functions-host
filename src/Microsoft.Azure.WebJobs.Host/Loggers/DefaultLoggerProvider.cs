// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class DefaultLoggerProvider : IHostInstanceLoggerProvider, IFunctionInstanceLoggerProvider
    {
        private readonly IStorageAccountProvider _storageAccountProvider;

        private bool _loggersSet;
        private IHostInstanceLogger _hostInstanceLogger;
        private IFunctionInstanceLogger _functionInstanceLogger;

        public DefaultLoggerProvider(IStorageAccountProvider storageAccountProvider)
        {
            if (storageAccountProvider == null)
            {
                throw new ArgumentNullException("storageAccountProvider");
            }

            _storageAccountProvider = storageAccountProvider;
        }

        async Task<IHostInstanceLogger> IHostInstanceLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            await EnsureLoggersAsync(cancellationToken);
            return _hostInstanceLogger;
        }

        async Task<IFunctionInstanceLogger> IFunctionInstanceLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            await EnsureLoggersAsync(cancellationToken);
            return _functionInstanceLogger;
        }

        private async Task EnsureLoggersAsync(CancellationToken cancellationToken)
        {
            if (_loggersSet)
            {
                return;
            }

            IStorageAccount dashboardAccount =
                await _storageAccountProvider.GetDashboardAccountAsync(cancellationToken);

            if (dashboardAccount != null)
            {
                // Create logging against a live Azure account.
                CloudBlobClient dashboardBlobClient = dashboardAccount.SdkObject.CreateCloudBlobClient();
                IPersistentQueueWriter<PersistentQueueMessage> queueWriter =
                    new PersistentQueueWriter<PersistentQueueMessage>(dashboardBlobClient);
                PersistentQueueLogger queueLogger = new PersistentQueueLogger(queueWriter);
                _hostInstanceLogger = queueLogger;
                _functionInstanceLogger = new CompositeFunctionInstanceLogger(queueLogger,
                    new ConsoleFunctionInstanceLogger());
            }
            else
            {
                // No auxillary logging. Logging interfaces are nops or in-memory.
                _hostInstanceLogger = new NullHostInstanceLogger();
                _functionInstanceLogger = new ConsoleFunctionInstanceLogger();
            }

            _loggersSet = true;
        }
    }
}
