// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Eventing.File
{
    internal static class FileChangeHelper
    {
        internal static async Task SetAppOfflineState(string rootPath, bool offline)
        {
            string path = Path.Combine(rootPath, ScriptConstants.AppOfflineFileName);
            bool offlineFileExists = System.IO.File.Exists(path);

            if (offline && !offlineFileExists)
            {
                // create the app_offline.htm file in the root script directory
                string content = FileUtility.ReadResourceString($"{ScriptConstants.ResourcePath}.{ScriptConstants.AppOfflineFileName}");
                await FileUtility.WriteAsync(path, content);
            }
            else if (!offline && offlineFileExists)
            {
                // delete the app_offline.htm file
                await Utility.InvokeWithRetriesAsync(() =>
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }, maxRetries: 3, retryInterval: TimeSpan.FromSeconds(1));
            }
        }

        internal static void TraceFileChangeRestart(ILogger logger, string changeDescription, string changeType, string path, bool isShutdown)
        {
            string fileChangeMsg = string.Format(CultureInfo.InvariantCulture, "{0} change of type '{1}' detected for '{2}'", changeDescription, changeType, path);
            logger.LogInformation(fileChangeMsg);

            string action = isShutdown ? "shutdown" : "restart";
            string signalMessage = $"Host configuration has changed. Signaling {action}";
            logger.LogInformation(signalMessage);
        }

        internal static void SignalShutdown(IScriptEventManager eventManager, string source, bool shouldDebounce = true)
        {
            eventManager.Publish(new HostShutdownEvent(source, shouldDebounce: shouldDebounce));
        }

        internal static void SignalRestart(IScriptEventManager eventManager, string source)
        {
            eventManager.Publish(new HostRestartEvent(source));
        }
    }
}
