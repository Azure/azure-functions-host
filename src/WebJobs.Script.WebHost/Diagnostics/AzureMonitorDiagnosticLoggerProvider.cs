// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class AzureMonitorDiagnosticLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly string _hostInstanceId;
        private readonly IEventGenerator _eventGenerator;
        private readonly IEnvironment _environment;
        private readonly HostNameProvider _hostNameProvider;
        private IExternalScopeProvider _scopeProvider;

        public AzureMonitorDiagnosticLoggerProvider(IOptions<ScriptJobHostOptions> scriptOptions, IEventGenerator eventGenerator, IEnvironment environment, HostNameProvider hostNameProvider)
            : this(scriptOptions.Value.InstanceId, eventGenerator, environment, hostNameProvider)
        {
        }

        public AzureMonitorDiagnosticLoggerProvider(string hostInstanceId, IEventGenerator eventGenerator, IEnvironment environment, HostNameProvider hostNameProvider)
        {
            _hostInstanceId = hostInstanceId ?? throw new ArgumentNullException(hostInstanceId);
            _eventGenerator = eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));
            _environment = environment ?? throw new ArgumentException(nameof(environment));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentException(nameof(hostNameProvider));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new AzureMonitorDiagnosticLogger(categoryName, _hostInstanceId, _eventGenerator, _environment, _scopeProvider, _hostNameProvider);
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
