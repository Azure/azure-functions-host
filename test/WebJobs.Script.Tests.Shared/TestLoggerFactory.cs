// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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

        public void AddProvider(ILoggerProvider provider)
        {
            Providers.Add(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}