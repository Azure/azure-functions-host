// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public sealed class DiagnosticListenerService : IHostedService, IDisposable
    {
        private readonly ILogger<DiagnosticListenerService> _logger;
        private readonly Dictionary<string, IDisposable> _listenerSubscriptions;
        private readonly IDisposable _standbyChangeHandler;
        private static bool _debugTraceEnabled;
        private IDisposable _allListenersSubscription;
        private bool _disposed;

        public DiagnosticListenerService(ILogger<DiagnosticListenerService> logger, IOptionsMonitor<StandbyOptions> standbyOptions)
        {
            _logger = logger;
            _listenerSubscriptions = new Dictionary<string, IDisposable>();

            SetDebugState();

            if (standbyOptions.CurrentValue.InStandbyMode)
            {
                _standbyChangeHandler = standbyOptions.OnChange(o => SetDebugState());
            }
        }

        private static void SetDebugState() => _debugTraceEnabled = FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableDebugTracing);

        private void SubscribeListeners()
        {
            if (_allListenersSubscription != null)
            {
                _allListenersSubscription.Dispose();
            }

            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(delegate (DiagnosticListener listener)
            {
                if (listener.Name.StartsWith(ScriptConstants.HostDiagnosticSourcePrefix)
                    || listener.Name.StartsWith(ApplicationInsightsDiagnosticConstants.ApplicationInsightsDiagnosticSourcePrefix))
                {
                    lock (_listenerSubscriptions)
                    {
                        IDisposable listenerSubscription = listener.Subscribe(new HostListenerObserver(listener.Name, _logger), IsEventEnabled);

                        if (!_listenerSubscriptions.TryAdd(listener.Name, listenerSubscription))
                        {
                            if (_listenerSubscriptions.Remove(listener.Name, out IDisposable existingSubscription))
                            {
                                existingSubscription.Dispose();
                            }

                            _listenerSubscriptions.Add(listener.Name, listenerSubscription);
                        }

                        _logger.LogInformation("Subscribed to diagnostic source '{sourceName}'", listener.Name);
                    }
                }
            });
        }

        private bool IsEventEnabled(string eventName)
        {
            return !eventName.StartsWith(ScriptConstants.HostDiagnosticSourceDebugEventNamePrefix) || _debugTraceEnabled;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            SubscribeListeners();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _allListenersSubscription?.Dispose();
            _standbyChangeHandler?.Dispose();

            foreach (var item in _listenerSubscriptions)
            {
                item.Value.Dispose();
            }

            _listenerSubscriptions.Clear();
            _disposed = true;
        }

        private class HostListenerObserver : IObserver<KeyValuePair<string, object>>
        {
            private readonly string _listenerName;
            private readonly ILogger _logger;

            public HostListenerObserver(string listenerName, ILogger logger)
            {
                _listenerName = listenerName;
                _logger = logger;
            }

            public void OnCompleted() { }

            public void OnError(Exception error) { }

            public void OnNext(KeyValuePair<string, object> kvp)
            {
                if (kvp.Value is IHostDiagnosticEvent diagnosticEvent)
                {
                    diagnosticEvent.LogEvent(_logger);
                }
                else
                {
                    _logger.LogDebug("Diagnostic source '{source}' emitted event '{eventName}': {payload}", _listenerName, kvp.Key, kvp.Value);
                }
            }
        }
    }
}
