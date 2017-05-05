// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class FileLoggerProvider : ILoggerProvider
    {
        private ScriptHostConfiguration _config;
        private Func<string, LogLevel, bool> _filter;

        public FileLoggerProvider(ScriptHostConfiguration config, Func<string, LogLevel, bool> filter)
        {
            _config = config;
            _filter = filter;
        }

        public ILogger CreateLogger(string categoryName)
        {
            switch (categoryName)
            {
                case ScriptConstants.LogCategoryHostNodeConsoleLogs:
                    return new FileLogger(categoryName, _config, _filter, (ConcurrentDictionary<string, TraceWriter> cache) =>
                    {
                        return cache.GetOrAdd(categoryName, (cat) => new CategoryTraceWriterFactory(cat, _config).Create());
                    });

                default:
                    return new FileLogger(categoryName, _config, _filter);
            }
        }

        public void Dispose()
        {
        }
    }
}
