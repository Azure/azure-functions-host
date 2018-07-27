// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.WebJobs.Script.Tests
{
    // TODO: Remove BrettSam
    //public class TestLoggerProviderFactory : ApplicationInsightsLoggerProviderFactory
    //{
    //    private readonly TestLoggerProvider _loggerProvider;
    //    private readonly bool _includeDefaultLoggerProviders;

    //    public TestLoggerProviderFactory(TestLoggerProvider loggerProvider, bool includeDefaultLoggerProviders = true)
    //    {
    //        _loggerProvider = loggerProvider;
    //        _includeDefaultLoggerProviders = includeDefaultLoggerProviders;
    //    }

    //    public override IEnumerable<ILoggerProvider> CreateLoggerProviders(string hostInstanceId, ScriptHostOptions scriptConfig, ScriptSettingsManager settingsManager, IMetricsLogger metricsLogger, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary)
    //    {
    //        return _includeDefaultLoggerProviders ?
    //            base.CreateLoggerProviders(hostInstanceId, scriptConfig, settingsManager, metricsLogger, isFileLoggingEnabled, isPrimary).Append(_loggerProvider) :
    //            new[] { _loggerProvider };
    //    }
    //}
}
