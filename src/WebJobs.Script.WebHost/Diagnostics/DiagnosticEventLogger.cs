// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEventLogger : ILogger, IDisposable
    {
        private readonly IDiagnosticEventRepositoryFactory _diagnosticEventRepositoryFactory;
        private readonly IEnvironment _environment;
        private readonly IDisposable _standbyChangeHandler;

        private IDiagnosticEventRepository _diagnosticEventRepository;
        private bool _isEnabled;

        public DiagnosticEventLogger(IDiagnosticEventRepositoryFactory diagnosticEventRepositoryFactory, IEnvironment environment,
            IOptionsMonitor<StandbyOptions> standbyOptions)
        {
            _diagnosticEventRepositoryFactory = diagnosticEventRepositoryFactory;
            _environment = environment;

            SetEnabledState(standbyOptions.CurrentValue);

            // Re-evaluate whether this is enabled after specialization
            if (standbyOptions.CurrentValue.InStandbyMode)
            {
                _standbyChangeHandler = standbyOptions.OnChange(o => SetEnabledState(o));
            }
        }

        private void SetEnabledState(StandbyOptions options)
        {
            _isEnabled = !options.InStandbyMode &&
                !FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableDiagnosticEventLogging, _environment);
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => _isEnabled;

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
                if (_diagnosticEventRepository == null)
                {
                    _diagnosticEventRepository = _diagnosticEventRepositoryFactory.Create();
                }
                _diagnosticEventRepository.WriteDiagnosticEvent(DateTime.UtcNow, stateInfo[ScriptConstants.ErrorCodeKey].ToString(), logLevel, message, stateInfo[ScriptConstants.HelpLinkKey].ToString(), exception);
            }
        }

        public void Dispose()
        {
            _standbyChangeHandler?.Dispose();
            (_diagnosticEventRepository as IDisposable)?.Dispose();
        }
    }
}
