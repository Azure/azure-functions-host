// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerConsoleLogService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly ILanguageWorkerConsoleLogSource _source;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _processingTask;
        private bool _disposed = false;

        public LanguageWorkerConsoleLogService(ILoggerFactory loggerFactory, ILanguageWorkerConsoleLogSource consoleLogSource)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _source = consoleLogSource ?? throw new ArgumentNullException(nameof(consoleLogSource));
            _logger = loggerFactory.CreateLogger(LanguageWorkerConstants.FunctionConsoleLogCategoryName);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _processingTask = ProcessLogs();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            await _processingTask;
        }

        private async Task ProcessLogs()
        {
            ISourceBlock<string> source = _source.LogStream;
            try
            {
                while (await source.OutputAvailableAsync(_cts.Token))
                {
                    _logger.LogInformation(await source.ReceiveAsync());
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