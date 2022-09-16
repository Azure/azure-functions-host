// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reactive.Linq;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class DebugStateProvider : IDebugStateProvider, IDisposable
    {
        internal const int DebugModeTimeoutMinutes = 15;
        internal const int DiagnosticModeTimeoutHours = 3;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _scriptOptions;
        private readonly IEnvironment _environment;
        private IDisposable _debugModeEvent;
        private IDisposable _diagnosticModeEvent;
        private bool _disposed;

        public DebugStateProvider(IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> scriptOptions, IScriptEventManager eventManager)
        {
            _environment = environment;
            _debugModeEvent = eventManager.OfType<DebugNotification>()
                .Subscribe(evt => LastDebugNotify = evt.NotificationTime);
            _diagnosticModeEvent = eventManager.OfType<DiagnosticNotification>()
                .Subscribe(evt => LastDiagnosticNotify = evt.NotificationTime);

            _scriptOptions = scriptOptions;
            _scriptOptions.OnChange(_ => InitializeLastNotificationTimes());

            InitializeLastNotificationTimes();
        }

        public DateTime LastDebugNotify { get; set; }

        public virtual bool InDebugMode => (DateTime.UtcNow - LastDebugNotify).TotalMinutes < DebugModeTimeoutMinutes;

        public DateTime LastDiagnosticNotify { get; set; }

        public virtual bool InDiagnosticMode => (DateTime.UtcNow - LastDiagnosticNotify).TotalHours < DiagnosticModeTimeoutHours;

        private void InitializeLastNotificationTimes()
        {
            Utility.ExecuteAfterColdStartDelay(_environment, () =>
            {
                LastDebugNotify = GetLastWriteTime(ScriptConstants.DebugSentinelFileName);
                LastDiagnosticNotify = GetLastWriteTime(ScriptConstants.DiagnosticSentinelFileName);
            });
        }

        private DateTime GetLastWriteTime(string fileName)
        {
            string hostLogPath = Path.Combine(_scriptOptions.CurrentValue.LogPath, "Host");
            string filePath = Path.Combine(hostLogPath, fileName);

            return File.Exists(filePath)
                ? File.GetLastWriteTimeUtc(filePath)
                : DateTime.MinValue;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _debugModeEvent.Dispose();
                _diagnosticModeEvent.Dispose();
                _disposed = true;
            }
        }
    }
}
