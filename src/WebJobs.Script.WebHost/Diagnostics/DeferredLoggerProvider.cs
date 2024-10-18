// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DeferredLoggerProvider : ILoggerProvider
    {
        private readonly Channel<DeferredLogEntry> _channel = Channel.CreateBounded<DeferredLogEntry>(new BoundedChannelOptions(150)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            // Avoids locks and interlocked operations when reading from the channel.
            SingleReader = true,
            SingleWriter = false
        });

        private bool _isEnabled = true;
        private bool _disposed = false;

        public int Count => _channel.Reader.Count;

        public ILogger CreateLogger(string categoryName)
        {
            if (_isEnabled)
            {
                return new DeferredLogger(_channel, categoryName);
            }
            else
            {
                return NullLogger.Instance;
            }
        }

        public void ProcessBufferedLogs(IEnumerable<ILoggerProvider> forwardingProviders, bool runImmediately = false)
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
                    if (!forwardingProviders.Any())
                    {
                        // No providers, just drain the messages without logging
                        while (_channel.Reader.TryRead(out _))
                        {
                            // Drain the channel
                        }
                    }

                    await foreach (var log in _channel.Reader.ReadAllAsync())
                    {
                        foreach (var forwardingProvider in forwardingProviders)
                        {
                            var logger = forwardingProvider.CreateLogger(log.Category);
                            if (string.IsNullOrEmpty(log.Scope))
                            {
                                logger.Log(log.LogLevel, log.EventId, log.Exception, log.Message);
                            }
                            else
                            {
                                logger.Log(log.LogLevel, log.EventId, log.Exception, $"{log.Scope} | {log.Message}");
                            }
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
                _isEnabled = false;
                // Signal channel completion
                _channel.Writer.TryComplete();
                _disposed = true;
            }
        }
    }
}