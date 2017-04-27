// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

        public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _config, _filter);

        public void Dispose()
        {
        }
    }
}
