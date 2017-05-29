// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class FileLoggerProvider : ILoggerProvider
    {
        private IFunctionTraceWriterFactory _traceWriterFactory;
        private Func<string, LogLevel, bool> _filter;

        public FileLoggerProvider(IFunctionTraceWriterFactory traceWriterFactory, Func<string, LogLevel, bool> filter)
        {
            _traceWriterFactory = traceWriterFactory;
            _filter = filter;
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _traceWriterFactory, _filter);

        public void Dispose()
        {
        }
    }
}
