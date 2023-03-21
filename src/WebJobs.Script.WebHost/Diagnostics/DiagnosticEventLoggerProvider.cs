// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLoggerProvider : ILoggerProvider
    {
        private readonly IDiagnosticEventRepositoryFactory _diagnosticEventRepositoryFactory;
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;

        public DiagnosticEventLoggerProvider(IDiagnosticEventRepositoryFactory diagnosticEventRepositoryFactory, IEnvironment environment,
            IOptionsMonitor<StandbyOptions> standbyOptions)
        {
            _diagnosticEventRepositoryFactory = diagnosticEventRepositoryFactory ?? throw new ArgumentNullException(nameof(diagnosticEventRepositoryFactory));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticEventLogger(_diagnosticEventRepositoryFactory, _environment, _standbyOptions);
        }

        public void Dispose()
        {
        }
    }
}
