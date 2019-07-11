// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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

            using (RecreateCapturedScope(logger, message.Scope))
            {
                logger.Log(message.LogLevel, message.EventId, message.State, message.Exception, message.Formatter);
            }
        }

        private IDisposable RecreateCapturedScope(ILogger logger, IEnumerable<object> scope)
        {
            IList<IDisposable> disposableScopes = scope.Select(s => logger.BeginScope(s)).ToList();

            // Allow loggers to know that these logs are deferred.
            disposableScopes.Add(logger.BeginScope(new Dictionary<string, object>
            {
                { ScriptConstants.LoggerDeferredLog, true },
            }));

            return Disposable.Create(() =>
            {
                foreach (IDisposable disposable in disposableScopes.Reverse())
                {
                    disposable.Dispose();
                }
            });
        }

        private async Task ProcessLogsAsync()
        {
            ChannelReader<DeferredLogMessage> channelReader = _deferredLogSource.LogChannel;

            try
            {
                while (await channelReader.WaitToReadAsync(_cts.Token))
                {
                    OnDeferredLog(await channelReader.ReadAsync(_cts.Token));
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