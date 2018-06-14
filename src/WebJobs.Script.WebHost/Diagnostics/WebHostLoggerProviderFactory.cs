// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostLoggerProviderFactory : DefaultLoggerProviderFactory
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly ScriptSettingsManager _settingsManager;

        public WebHostLoggerProviderFactory(IEventGenerator eventGenerator, ScriptSettingsManager settingsManager)
        {
            _eventGenerator = eventGenerator;
            _settingsManager = settingsManager;
        }

        public override IEnumerable<ILoggerProvider> CreateLoggerProviders(string hostInstanceId, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager, IMetricsLogger metricsLogger, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary)
        {
            // TODO: DI (FACAVAL) Review
            //ILoggerProvider systemProvider = new SystemLoggerProvider(hostInstanceId, _eventGenerator, _settingsManager);

            return base.CreateLoggerProviders(hostInstanceId, scriptConfig, settingsManager, metricsLogger, isFileLoggingEnabled, isPrimary);
        }
    }
}
