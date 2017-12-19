// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestLoggerProviderFactory : DefaultLoggerProviderFactory
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly bool _includeDefaultLoggerProviders;

        public TestLoggerProviderFactory(TestLoggerProvider loggerProvider, bool includeDefaultLoggerProviders = true)
        {
            _loggerProvider = loggerProvider;
            _includeDefaultLoggerProviders = includeDefaultLoggerProviders;
        }

        public override IEnumerable<ILoggerProvider> CreateLoggerProviders(ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager, Func<bool> fileLoggingEnabled, Func<bool> isPrimary)
        {
            return _includeDefaultLoggerProviders ?
                base.CreateLoggerProviders(scriptConfig, settingsManager, fileLoggingEnabled, isPrimary).Append(_loggerProvider) :
                new[] { _loggerProvider };
        }
    }
}
