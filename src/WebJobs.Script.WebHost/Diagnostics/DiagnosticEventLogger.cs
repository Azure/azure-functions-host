// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLogger : ILogger
    {
        private const string ErrorCode = "errorCode";
        private const string HelpLink = "helpLink";
        private readonly IDiagnosticEventRepositoryFactory _diagnosticEventRepositoryFactory;
        private readonly IEnvironment _environment;
        private bool _isSpecialized = false;
        private IDiagnosticEventRepository _diagnosticEventRepository;

        public DiagnosticEventLogger(IDiagnosticEventRepositoryFactory diagnosticEventRepositoryFactory, IEnvironment environment)
        {
            _diagnosticEventRepositoryFactory = diagnosticEventRepositoryFactory;
            _environment = environment;
        }

        public IDiagnosticEventRepository DiagnosticEventRepository
        {
            get
            {
                if (_diagnosticEventRepository == null)
                {
                    _diagnosticEventRepository = _diagnosticEventRepositoryFactory.Create();
                }
                return _diagnosticEventRepository;
            }
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (!_isSpecialized)
            {
                _isSpecialized = !_environment.IsPlaceholderModeEnabled();
            }

            return _isSpecialized;
        }

        private bool IsDiagnosticEvent(IDictionary<string, object> state)
        {
            return state.Keys.Contains(ScriptConstants.DiagnosticEventKey, StringComparer.OrdinalIgnoreCase);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (state is IDictionary<string, object> stateInfo && IsDiagnosticEvent(stateInfo))
            {
                string message = formatter(state, exception);
                DiagnosticEventRepository.WriteDiagnosticEvent(DateTime.UtcNow, stateInfo[ErrorCode].ToString(), logLevel, message, stateInfo[HelpLink].ToString(), exception);
            }
        }
    }
}
