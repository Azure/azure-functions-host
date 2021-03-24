// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLoggerProvider : ILoggerProvider
    {
        private readonly IDiagnosticEventRepository _diagnosticEventRepository;
        private readonly IEnvironment _environment;

        public DiagnosticEventLoggerProvider(IDiagnosticEventRepository diagnosticEventRepository, IEnvironment environment)
        {
            _diagnosticEventRepository = diagnosticEventRepository ?? throw new ArgumentNullException(nameof(diagnosticEventRepository));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticEventLogger(_diagnosticEventRepository, _environment);
        }

        public void Dispose()
        {
        }
    }
}
