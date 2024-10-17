// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DeferredLogDispatcher : IDeferredLogDispatcher, IDisposable
    {
        private readonly Channel<DeferredLogEntry> _channel = Channel.CreateBounded<DeferredLogEntry>(new BoundedChannelOptions(150)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            // Avoids locks and interlocked operations when reading and writing to the channel.
            SingleReader = true,
            SingleWriter = true
        });

        private readonly List<ILoggerProvider> _forwardingProviders = new(1);
        private bool _isEnabled = true;
        private bool _disposed = false;

        public int Count => _channel.Reader.Count;

        bool IDeferredLogDispatcher.IsEnabled => _isEnabled;

        public void AddLoggerProvider(ILoggerProvider provider)
        {
            _forwardingProviders.Add(provider);
        }

        public void Log(DeferredLogEntry log)
        {
            _channel.Writer.TryWrite(log);
        }

        public void ProcessBufferedLogs(bool runImmediately = false)
        {
            // Disable the channel and let the consumer know that there won't be any more logs.
            _isEnabled = false;
            _channel.Writer.TryComplete();

            // Forward all buffered logs to the new provider
            Task.Run(async () =>
            {
                if (!runImmediately)
                {
                    // Wait for 10 seconds, this will increase the probability of these logs appearing in live metrics.
                    await Task.Delay(10000);
                }

                try
                {
                    await foreach (var log in _channel.Reader.ReadAllAsync())
                    {
                        if (_forwardingProviders.Count == 0)
                        {
                            // No providers, just drain the messages without logging
                            continue;
                        }

                        foreach (var forwardingProvider in _forwardingProviders)
                        {
                            var logger = forwardingProvider.CreateLogger(log.Category);
                            logger.Log(log.LogLevel, log.EventId, log.Exception, log.Message);
                        }
                    }
                }
                catch
                {
                    // Ignore any exception.
                }
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Stop accepting logs
                _isEnabled = false;

                // Signal channel completion
                _channel.Writer.TryComplete();
            }
        }
    }
}