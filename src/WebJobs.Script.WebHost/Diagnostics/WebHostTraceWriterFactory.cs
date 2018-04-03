// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class WebHostTraceWriterFactory : IHostTraceWriterFactory
    {
        private IEventGenerator _eventGenerator;
        private ScriptSettingsManager _settingsManager;

        public WebHostTraceWriterFactory(IEventGenerator eventGenerator, ScriptSettingsManager settingsManager)
        {
            _eventGenerator = eventGenerator;
            _settingsManager = settingsManager;
        }

        public TraceWriter Create(TraceLevel level)
        {
            return new DiagnosticTraceWriter(_eventGenerator, _settingsManager, level);
        }
    }
}