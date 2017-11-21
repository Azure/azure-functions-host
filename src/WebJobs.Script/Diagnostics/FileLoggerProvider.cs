// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;
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

        public ILogger CreateLogger(string categoryName)
        {
            switch (categoryName)
            {
                case string cat when WorkerLogger.Regex.IsMatch(cat):
                    return new WorkerLogger(cat, _traceWriterFactory, _filter);
                case string cat when cat == LogCategories.Function:
                    return new FunctionLogger(cat, _traceWriterFactory, _filter);
                default:
                    // essentially a void logger
                    return new FunctionLogger(categoryName, _traceWriterFactory, (cat, lvl) => false);
            }
        }

        public void Dispose()
        {
        }
    }
}
