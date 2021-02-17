// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLoggerProvider : ILoggerProvider
    {
        private readonly IDiagnosticEventRepository _diagnosticEventRepository;

        public DiagnosticEventLoggerProvider(IDiagnosticEventRepository diagnosticEventRepository)
        {
            _diagnosticEventRepository = diagnosticEventRepository;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticEventLogger(_diagnosticEventRepository);
        }

        public void Dispose()
        {
        }
    }
}
