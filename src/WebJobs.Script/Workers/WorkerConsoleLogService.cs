// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerConsoleLogService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IWorkerConsoleLogSource _source;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _processingTask;
        private bool _disposed = false;

        public WorkerConsoleLogService(ILoggerFactory loggerFactory, IWorkerConsoleLogSource consoleLogSource)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _source = consoleLogSource ?? throw new ArgumentNullException(nameof(consoleLogSource));
            _logger = loggerFactory.CreateLogger(WorkerConstants.ConsoleLogCategoryName);
        }

        internal WorkerConsoleLogService(ILogger logger, IWorkerConsoleLogSource consoleLogSource)
        {
            _source = consoleLogSource ?? throw new ArgumentNullException(nameof(consoleLogSource));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _processingTask = ProcessLogs();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();

            if (_processingTask != null)
            {
                await _processingTask;
            }
        }

        internal async Task ProcessLogs()
        {
            ISourceBlock<ConsoleLog> source = _source.LogStream;
            try
            {
                while (await source.OutputAvailableAsync(_cts.Token))
                {
                    var consoleLog = await source.ReceiveAsync();
                    _logger.Log(consoleLog.Level, consoleLog.Message);
                }
            }
            catch (OperationCanceledException)
            {
                // This occurs during shutdown.
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cts?.Dispose();
                _disposed = true;
            }
        }
    }
}