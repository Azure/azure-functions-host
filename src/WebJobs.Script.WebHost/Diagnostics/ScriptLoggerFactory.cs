// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ScriptLoggerFactory : ILoggerFactory
    {
        private readonly LoggerFactory _factory;

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
                return _factory.CreateLogger(categoryName);
            }
            catch (ObjectDisposedException ex)
            {
                throw new HostDisposedException(typeof(ScriptLoggerFactory).FullName, ex);
            }
        }

        public void Dispose()
        {
            _factory.Dispose();
        }
    }
}
