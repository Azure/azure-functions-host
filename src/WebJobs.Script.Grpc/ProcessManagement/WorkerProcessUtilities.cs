// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal static class WorkerProcessUtilities
    {
        private static int maxNumberOfErrorMessages = 3;

        public static bool IsConsoleLog(string msg)
        {
            if (msg.StartsWith(WorkerConstants.LanguageWorkerConsoleLogPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        public static Queue<string> AddStdErrMessage(Queue<string> processStdErrDataQueue, string msg)
        {
            if (processStdErrDataQueue.Count >= maxNumberOfErrorMessages)
            {
                processStdErrDataQueue.Dequeue();
                processStdErrDataQueue.Enqueue(msg);
            }
            else
            {
                processStdErrDataQueue.Enqueue(msg);
            }
            return processStdErrDataQueue;
        }

        public static string RemoveLogPrefix(string msg)
        {
            return Regex.Replace(msg, WorkerConstants.LanguageWorkerConsoleLogPrefix, string.Empty, RegexOptions.IgnoreCase);
        }

        public static bool IsToolingConsoleJsonLogEntry(string msg)
        {
            return msg.StartsWith(WorkerConstants.ToolingConsoleLogPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
