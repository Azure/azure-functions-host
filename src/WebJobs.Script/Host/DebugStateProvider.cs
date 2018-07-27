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
        private IDisposable _event;
        private bool _disposed;

        public DebugStateProvider(IOptions<ScriptHostOptions> scriptOptions, IScriptEventManager eventManager)
        {
            _event = eventManager.OfType<DebugNotification>()
                .Subscribe(evt => LastDebugNotify = evt.NotificationTime);

            string hostLogPath = Path.Combine(scriptOptions.Value.RootLogPath, "Host");

            string debugSentinelFileName = Path.Combine(hostLogPath, ScriptConstants.DebugSentinelFileName);
            LastDebugNotify = File.Exists(debugSentinelFileName)
                ? File.GetLastWriteTimeUtc(debugSentinelFileName)
                : DateTime.MinValue;
        }

        public DateTime LastDebugNotify { get; set; }

        /// <summary>
        /// Gets a value indicating whether the host is in debug mode.
        /// </summary>
        public virtual bool InDebugMode
        {
            get
            {
                return (DateTime.UtcNow - LastDebugNotify).TotalMinutes < DebugModeTimeoutMinutes;
            }
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
