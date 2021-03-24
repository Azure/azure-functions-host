// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLogger : ILogger
    {
        private const string ErrorCode = "errorCode";
        private const string HelpLink = "helpLink";
        private readonly IDiagnosticEventRepository _diagnosticEventRepository;
        private readonly IEnvironment _environment;
        private bool _isSpecialized = false;

        public DiagnosticEventLogger(IDiagnosticEventRepository actionableEventRepository, IEnvironment environment)
        {
            _diagnosticEventRepository = actionableEventRepository;
            _environment = environment;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            Console.WriteLine($"**** DiagnosticEventLogger Before_isSpecialized:{_isSpecialized}");
            if (!_isSpecialized)
            {
                _isSpecialized = !_environment.IsPlaceholderModeEnabled();
                Console.WriteLine($"**** DiagnosticEventLogger After_isSpecialized:{_isSpecialized}");
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
                Console.WriteLine("**** DiagnosticEventLogger not specialized yet");
                return;
            }

            Console.WriteLine("**** DiagnosticEventLogger specialized now");
            if (state is IDictionary<string, object> stateInfo && IsDiagnosticEvent(stateInfo))
            {
                string message = formatter(state, exception);
                Console.WriteLine($"**** Logging: {message}");
                _diagnosticEventRepository.AddDiagnosticEvent(DateTime.UtcNow, stateInfo[ErrorCode].ToString(), logLevel, message, stateInfo[HelpLink].ToString(), exception);
            }
        }
    }
}
