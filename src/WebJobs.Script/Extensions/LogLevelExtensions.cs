// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    internal static class LogLevelExtensions
    {
        internal static TraceLevel ToTraceLevel(this LogLevel logLevel)
        {
            TraceLevel level = TraceLevel.Off;
            switch (logLevel)
            {
                case LogLevel.Critical:
                case LogLevel.Error:
                    level = TraceLevel.Error;
                    break;

                case LogLevel.Trace:
                case LogLevel.Debug:
                    level = TraceLevel.Verbose;
                    break;

                case LogLevel.Information:
                    level = TraceLevel.Info;
                    break;

                case LogLevel.Warning:
                    level = TraceLevel.Warning;
                    break;

                default:
                    break;
            }
            return level;
        }
    }
}