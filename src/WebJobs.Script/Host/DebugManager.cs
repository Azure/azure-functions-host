// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class DebugManager : IDebugManager
    {
        internal const int DebugModeTimeoutMinutes = 15;
        private readonly IDebugStateProvider _debugStateProvider;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _scriptOptions;
        private readonly ILogger _logger;

        public DebugManager(IOptionsMonitor<ScriptApplicationHostOptions> scriptOptions, IDebugStateProvider debugStateProvider,
            IScriptEventManager eventManager, ILogger<DebugManager> logger)
        {
            _debugStateProvider = debugStateProvider;
            _scriptOptions = scriptOptions;
            _logger = logger;
        }

        /// <summary>
        /// Notifies this host that it should be in debug mode.
        /// </summary>
        public void NotifyDebug()
        {
            // This is redundant, since we're also watching the debug marker
            // file. However, we leave this here for assurances.
            _debugStateProvider.LastDebugNotify = DateTime.UtcNow;

            try
            {
                // create or update the debug sentinel file to trigger a
                // debug timeout update across all instances
                string debugSentinelFileName = Path.Combine(_scriptOptions.CurrentValue.LogPath, "Host", ScriptConstants.DebugSentinelFileName);
                if (!File.Exists(debugSentinelFileName))
                {
                    File.WriteAllText(debugSentinelFileName, "This is a system managed marker file used to control runtime debug mode behavior.");
                }
                else
                {
                    File.SetLastWriteTimeUtc(debugSentinelFileName, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                // best effort
                _logger.DebugerManagerUnableToUpdateSentinelFile(ex);

                if (ex.IsFatal())
                {
                    throw;
                }
            }
        }
    }
}
