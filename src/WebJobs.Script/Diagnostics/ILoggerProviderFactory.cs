// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides ways to plug into the ScriptHost ILoggerFactory initialization.
    /// </summary>
    public interface ILoggerProviderFactory
    {
        IEnumerable<ILoggerProvider> CreateLoggerProviders(string hostInstanceId,
            ScriptHostOptions scriptConfig,
            ScriptSettingsManager settingsManager,
            IMetricsLogger metricsLogger,
            Func<bool> isFileLoggingEnabled,
            Func<bool> isPrimary);
    }
}
