// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class DefaultLoggerProvider : IHostInstanceLoggerProvider, IFunctionInstanceLoggerProvider, IFunctionOutputLoggerProvider
    {
        private readonly IStorageAccountProvider _storageAccountProvider;

        private bool _loggersSet;
        private IHostInstanceLogger _hostInstanceLogger;
        private IFunctionInstanceLogger _functionInstanceLogger;
        private IFunctionOutputLogger _functionOutputLogger;
        private TraceWriter _trace;

        public DefaultLoggerProvider(IStorageAccountProvider storageAccountProvider, TraceWriter trace)
        {
            if (storageAccountProvider == null)
            {
                throw new ArgumentNullException("storageAccountProvider");
            }
            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            _storageAccountProvider = storageAccountProvider;
            _trace = trace;
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

        async Task<IFunctionOutputLogger> IFunctionOutputLoggerProvider.GetAsync(CancellationToken cancellationToken)
        {
            await EnsureLoggersAsync(cancellationToken);
            return _functionOutputLogger;
        }

        private async Task EnsureLoggersAsync(CancellationToken cancellationToken)
        {
            if (_loggersSet)
            {
                return;
            }

            IStorageAccount dashboardAccount = await _storageAccountProvider.GetDashboardAccountAsync(cancellationToken);
            IStorageAccount storageAccount = await _storageAccountProvider.GetStorageAccountAsync(cancellationToken);
            IFunctionInstanceLogger traceWriterFunctionLogger = new TraceWriterFunctionInstanceLogger(_trace);

            if (dashboardAccount != null)
            {
                // Create logging against a live Azure account.
                IStorageBlobClient dashboardBlobClient = dashboardAccount.CreateBlobClient();
                IPersistentQueueWriter<PersistentQueueMessage> queueWriter = new PersistentQueueWriter<PersistentQueueMessage>(dashboardBlobClient);
                PersistentQueueLogger queueLogger = new PersistentQueueLogger(queueWriter);
                _hostInstanceLogger = queueLogger;
                _functionInstanceLogger = new CompositeFunctionInstanceLogger(queueLogger, traceWriterFunctionLogger);
                _functionOutputLogger = new BlobFunctionOutputLogger(dashboardBlobClient);
            }
            else
            {
                // No auxillary logging. Logging interfaces are nops or in-memory.
                _hostInstanceLogger = new NullHostInstanceLogger();
                _functionInstanceLogger = traceWriterFunctionLogger;
                _functionOutputLogger = new ConsoleFunctionOutputLogger();
            }

            _loggersSet = true;
        }
    }
}
