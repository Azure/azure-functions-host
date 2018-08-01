﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.Logging;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestLoggerProvider : ILoggerProvider
    {
        private readonly Func<string, LogLevel, bool> _filter;

        public TestLoggerProvider(Func<string, LogLevel, bool> filter = null)
        {
            _filter = filter;
        }

        private ConcurrentDictionary<string, TestLogger> LoggerCache { get; } = new ConcurrentDictionary<string, TestLogger>();

        public IEnumerable<TestLogger> CreatedLoggers => LoggerCache.Values;

        public ILogger CreateLogger(string categoryName)
        {
            return LoggerCache.GetOrAdd(categoryName, (key) => new TestLogger(key, _filter));
        }

        public IList<LogMessage> GetAllLogMessages() => CreatedLoggers.SelectMany(l => l.GetLogMessages()).OrderBy(p => p.Timestamp).ToList();

        public void ClearAllLogMessages()
        {
            foreach (TestLogger logger in CreatedLoggers)
            {
                logger.ClearLogMessages();
            }
        }

        public void Dispose()
        {
        }
    }
}
