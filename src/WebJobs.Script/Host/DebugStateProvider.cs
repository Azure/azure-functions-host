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
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _scriptOptions;
        private IDisposable _event;
        private bool _disposed;

        public DebugStateProvider(IOptionsMonitor<ScriptApplicationHostOptions> scriptOptions, IScriptEventManager eventManager)
        {
            _event = eventManager.OfType<DebugNotification>()
                .Subscribe(evt => LastDebugNotify = evt.NotificationTime);

            _scriptOptions = scriptOptions;

            InitializeLastDebugNotify();

            // If these settings changes, we need to refresh our LastDebugNotify value
            _scriptOptions.OnChange(_ => InitializeLastDebugNotify());
        }

        public DateTime LastDebugNotify { get; set; }

        /// <summary>
        /// Gets a value indicating whether the host is in debug mode.
        /// </summary>
        public virtual bool InDebugMode => (DateTime.UtcNow - LastDebugNotify).TotalMinutes < DebugModeTimeoutMinutes;

        private void InitializeLastDebugNotify()
        {
            string hostLogPath = Path.Combine(_scriptOptions.CurrentValue.LogPath, "Host");

            string debugSentinelFileName = Path.Combine(hostLogPath, ScriptConstants.DebugSentinelFileName);
            LastDebugNotify = File.Exists(debugSentinelFileName)
                ? File.GetLastWriteTimeUtc(debugSentinelFileName)
                : DateTime.MinValue;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _event.Dispose();
                _disposed = true;
            }
        }
    }
}
