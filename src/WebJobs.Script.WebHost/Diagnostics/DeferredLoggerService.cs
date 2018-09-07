// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class DeferredLoggerService : IHostedService
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IDeferredLogSource _deferredLogSource;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private Task _processingTask;

        public DeferredLoggerService(IDeferredLogSource deferredLogSource, ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _deferredLogSource = deferredLogSource;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _processingTask = ProcessLogsAsync();
            return Task.CompletedTask;
        }

        private void OnDeferredLog(DeferredLogMessage message)
        {
            ILogger logger = _loggerFactory.CreateLogger(message.Category);

            // Allow loggers to know that these logs are deferred. They may ignore them or apply the
            // supplied timestamp as the real time.
            using (logger.BeginScope(new Dictionary<string, object>
            {
                { ScriptConstants.LoggerDeferredLog, true },
                { ScriptConstants.LoggerTimestamp, message.Timestamp }
            }))
            {
                logger.Log(message.LogLevel, message.EventId, message.State, message.Exception, message.Formatter);
            }
        }

        private async Task ProcessLogsAsync()
        {
            ISourceBlock<DeferredLogMessage> buffer = _deferredLogSource.LogBuffer;
            try
            {
                while (await buffer.OutputAvailableAsync(_cts.Token))
                {
                    OnDeferredLog(await buffer.ReceiveAsync());
                }
            }
            catch (OperationCanceledException)
            {
                // This happens when StopAsync is called.
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cts.Cancel();
            await _processingTask;
        }
    }
}
