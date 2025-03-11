// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public sealed class DeferredLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly Channel<DeferredLogEntry> _channel = Channel.CreateBounded<DeferredLogEntry>(new BoundedChannelOptions(150)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            // Avoids locks and interlocked operations when reading from the channel.
            SingleReader = true,
            SingleWriter = false
        });

        private readonly IEnvironment _environment;
        private IExternalScopeProvider _scopeProvider;
        private bool _isEnabled = true;

        public DeferredLoggerProvider(IEnvironment environment)
        {
            _environment = environment;
        }

        public int Count => _channel.Reader.Count;

        public ILogger CreateLogger(string categoryName)
        {
            return _isEnabled ? new DeferredLogger(_channel, categoryName, _scopeProvider, _environment) : NullLogger.Instance;
        }

        public async Task ProcessBufferedLogsAsync(IReadOnlyCollection<ILoggerProvider> forwardingProviders)
        {
            // Forward all buffered logs to the new provider
            try
            {
                if (forwardingProviders is null || forwardingProviders.Count == 0)
                {
                    // No providers, just drain the messages without logging and disable the channel.
                    _isEnabled = false;
                    _channel.Writer.TryComplete();
                    while (_channel.Reader.TryRead(out _))
                    {
                        // Drain the channel
                    }
                    return;
                }

                while (await _channel.Reader.WaitToReadAsync())
                {
                    while (_channel.Reader.TryRead(out var log))
                    {
                        foreach (var forwardingProvider in forwardingProviders)
                        {
                            var logger = forwardingProvider.CreateLogger(log.Category);
                            if (log.ScopeStorage?.Count > 0)
                            {
                                ProcessLogWithScope(logger, log);
                            }
                            else
                            {
                                // No scopes
                                logger.Log(log.LogLevel, log.EventId, log.Exception, log.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // Ignore exceptions
            }
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        private static void ProcessLogWithScope(ILogger logger, DeferredLogEntry log)
        {
            List<IDisposable> scopes = null;
            try
            {
                // Create a scope for each object in ScopeObject
                scopes ??= new List<IDisposable>();
                foreach (var scope in log.ScopeStorage)
                {
                    // Create and store each scope
                    scopes.Add(logger.BeginScope(scope));
                }

                // Log the message
                logger.Log(log.LogLevel, log.EventId, log.Exception, log.Message);
            }
            finally
            {
                if (scopes is not null)
                {
                    // Dispose all scopes in reverse order to properly unwind them
                    for (int i = scopes.Count - 1; i >= 0; i--)
                    {
                        scopes[i].Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            _isEnabled = false;
            _channel.Writer.TryComplete();
        }
    }
}