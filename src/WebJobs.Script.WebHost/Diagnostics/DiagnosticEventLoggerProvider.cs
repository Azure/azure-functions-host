// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLoggerProvider : ILoggerProvider
    {
        private readonly IDiagnosticEventRepositoryFactory _diagnosticEventRepositoryFactory;
        private readonly IEnvironment _environment;

        public DiagnosticEventLoggerProvider(IDiagnosticEventRepositoryFactory diagnosticEventRepositoryFactory, IEnvironment environment)
        {
            _diagnosticEventRepositoryFactory = diagnosticEventRepositoryFactory ?? throw new ArgumentNullException(nameof(diagnosticEventRepositoryFactory));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticEventLogger(_diagnosticEventRepositoryFactory, _environment);
        }

        public void Dispose()
        {
        }
    }
}
