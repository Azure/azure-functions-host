// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ScriptLoggerFactory : ILoggerFactory
    {
        private readonly LoggerFactory _factory;
        private readonly ConcurrentDictionary<string, ILogger> _loggerCache = new ConcurrentDictionary<string, ILogger>();

        public ScriptLoggerFactory(IEnumerable<ILoggerProvider> providers, IOptionsMonitor<LoggerFilterOptions> filterOption)
        {
            _factory = new LoggerFactory(providers, filterOption);
        }

        public void AddProvider(ILoggerProvider provider)
        {
            _factory.AddProvider(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return CreateLoggerInternal(categoryName);
        }

        internal virtual ILogger CreateLoggerInternal(string categoryName)
        {
            try
            {
                // To avoid the lock contention in factory.CreateLogger, cache the result
                // This is safe because:
                // a) The factory isn't changing in this instance anyway
                // b) This singleton gets re-created with every new JobHost as it exists in the child service scope
                if (_loggerCache.TryGetValue(categoryName, out var result))
                {
                    return result;
                }
                return _loggerCache[categoryName] = _factory.CreateLogger(categoryName);
            }
            catch (ObjectDisposedException ex)
            {
                throw new HostDisposedException(typeof(ScriptLoggerFactory).FullName, ex);
            }
        }

        public void Dispose()
        {
            _factory.Dispose();
            // We also clear the cache here so that any future callers will hit a HostDisposedException as expected
            _loggerCache.Clear();
        }
    }
}
