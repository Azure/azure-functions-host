// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Metrics;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class MeterLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly IEnvironment _environment;
        private readonly Meter meter;
        private IExternalScopeProvider _scopeProvider;

        public MeterLoggerProvider(IEnvironment environment, IMeterFactory meterFactory)
        {
            meter = meterFactory.Create("Azure.Functions");
            _environment = environment ?? throw new ArgumentException(nameof(environment));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new MeterLogger(categoryName, _environment, _scopeProvider, meter);
        }

        public void Dispose()
        {
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}