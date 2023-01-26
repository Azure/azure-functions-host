// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public static class DiagnosticEventLoggerExtensions
    {
        public static void LogDiagnosticEvent(this ILogger logger, LogLevel level, int eventId, string errorCode, string message, string helpLink, Exception exception)
        {
            var stateDict = new Dictionary<string, object>
            {
                { ScriptConstants.DiagnosticEventKey, string.Empty },
                { ScriptConstants.HelpLinkKey, helpLink },
                { ScriptConstants.ErrorCodeKey, errorCode }
            };

            logger.Log(level, eventId, stateDict, exception, (state, ex) => message);
        }

        public static void LogDiagnosticEventInformation(this ILogger logger, string errorCode, string message, string helpLink)
        {
            int eventId = 0; // Dummy value - diagnostic events does not log an eventId.
            logger.LogDiagnosticEvent(LogLevel.Information, eventId, errorCode, message, helpLink, null);
        }

        public static void LogDiagnosticEventError(this ILogger logger, string errorCode, string message, string helpLink, Exception exception)
        {
            int eventId = 0; // Dummy value - diagnostic events does not log an eventId.
            logger.LogDiagnosticEvent(LogLevel.Error, eventId, errorCode, message, helpLink, exception);
        }
    }
}
