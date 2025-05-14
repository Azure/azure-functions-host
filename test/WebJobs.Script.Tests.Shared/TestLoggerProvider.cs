// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.Logging;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopeProvider;
        private ConcurrentDictionary<string, TestLogger> _loggerCache = new ConcurrentDictionary<string, TestLogger>();

        public TestLoggerProvider(string hostInstanceId = null)
        {
            HostInstanceId = hostInstanceId;
        }

        public IEnumerable<TestLogger> CreatedLoggers => _loggerCache.Values;

        // settable by tests
        public string HostInstanceId { get; set; }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggerCache.GetOrAdd(categoryName, (key) => new TestLogger(key, HostInstanceId, _scopeProvider));
        }

        public IList<LogMessage> GetAllLogMessages()
        {
            return CreatedLoggers.SelectMany(l => l.GetLogMessages()).OrderBy(p => p.Timestamp).ToList();
        }

        /// <summary>
        /// Returns a single string that contains all log message strings on a separate line.
        /// </summary>
        /// <returns>The log message.</returns>
        public string GetLog()
        {
            return string.Join(Environment.NewLine, GetAllLogMessages());
        }

        public void ClearAllLogMessages()
        {
            foreach (TestLogger logger in CreatedLoggers)
            {
                logger.ClearLogMessages();
            }
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
        }
    }
}
