// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestLoggerFactory : ILoggerFactory
    {
        public TestLoggerFactory()
        {
            Providers = new List<ILoggerProvider>();
        }

        public IList<ILoggerProvider> Providers { get; private set; }

        public IList<TestLogger> CreatedLoggers { get; } = new List<TestLogger>();

        public void AddProvider(ILoggerProvider provider)
        {
            Providers.Add(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new TestLogger(categoryName);
            CreatedLoggers.Add(logger);
            return logger;
        }

        public IEnumerable<LogMessage> GetAllLogMessages()
        {
            return CreatedLoggers.SelectMany(l => l.LogMessages);
        }

        public void Dispose()
        {
        }
    }
}
