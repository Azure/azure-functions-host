// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class FilteringLoggerFactory : ILoggerFactory
    {
        private readonly LoggerFactory _factory;

        public FilteringLoggerFactory(IEnumerable<ILoggerProvider> providers, IOptionsMonitor<LoggerFilterOptions> filterOption)
        {
            _factory = new LoggerFactory(providers, filterOption);
        }

        public void AddProvider(ILoggerProvider provider)
        {
            _factory.AddProvider(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            ILogger logger = _factory.CreateLogger(categoryName);

            if (string.Equals(categoryName, "Microsoft.AspNetCore.Hosting.Internal.WebHost", StringComparison.Ordinal))
            {
                return new AspNetWebHostFilteringLogger(logger);
            }

            return logger;
        }

        public void Dispose()
        {
            _factory.Dispose();
        }
    }
}
