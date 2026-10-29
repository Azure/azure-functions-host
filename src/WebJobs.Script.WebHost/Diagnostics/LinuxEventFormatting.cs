// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal static class LinuxEventFormatting
    {
        internal const string EventTimestampFormat = "O";

        internal static string NormalizeString(string value, bool addEnclosingQuotes = true)
        {
            value = value.Replace(Environment.NewLine, " ");
            value = value.Replace("\"", "'");

            return addEnclosingQuotes ? $"\"{value}\"" : value;
        }

        internal static EventLevel ToEventLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace or LogLevel.Debug => EventLevel.Verbose,
                LogLevel.Information => EventLevel.Informational,
                LogLevel.Warning => EventLevel.Warning,
                LogLevel.Error => EventLevel.Error,
                LogLevel.Critical => EventLevel.Critical,
                _ => EventLevel.LogAlways
            };
        }
    }
}
