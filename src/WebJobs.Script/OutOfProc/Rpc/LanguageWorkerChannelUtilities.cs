// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.OutOfProc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal static class LanguageWorkerChannelUtilities
    {
        private static int maxNumberOfErrorMessages = 3;

        public static bool IsLanguageWorkerConsoleLog(string msg)
        {
            if (msg.StartsWith(OutOfProcConstants.LanguageWorkerConsoleLogPrefix, StringComparison.OrdinalIgnoreCase))
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
            return Regex.Replace(msg, OutOfProcConstants.LanguageWorkerConsoleLogPrefix, string.Empty, RegexOptions.IgnoreCase);
        }
    }
}
