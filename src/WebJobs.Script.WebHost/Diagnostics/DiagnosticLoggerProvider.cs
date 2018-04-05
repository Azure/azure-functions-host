// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticLoggerProvider : ILoggerProvider
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly Func<string, LogLevel, bool> _filter;

        public DiagnosticLoggerProvider(IEventGenerator eventGenerator, ScriptSettingsManager settingsManager, Func<string, LogLevel, bool> filter)
        {
            _eventGenerator = eventGenerator;
            _settingsManager = settingsManager;
            _filter = filter;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticLogger(categoryName, _eventGenerator, _settingsManager, _filter);
        }

        public void Dispose()
        {
        }
    }
}
