// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class HostErrorLoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;

        public HostErrorLoggerProvider(Action<Exception> handleHostError)
        {
            _logger = new HostErrorLogger(handleHostError);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _logger;
        }

        public void Dispose()
        {
        }
    }
}
