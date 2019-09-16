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
