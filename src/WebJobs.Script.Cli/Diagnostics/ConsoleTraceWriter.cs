// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.Azure.WebJobs.Host;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Diagnostics
{
    public class ConsoleTraceWriter : TraceWriter
    {
        public ConsoleTraceWriter(TraceLevel level) : base(level)
        {
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level >= traceEvent.Level)
            {
                ColoredConsole.WriteLine(GetMessageString(traceEvent));
            }
        }

        private static RichString GetMessageString(TraceEvent traceEvent)
        {
            switch (traceEvent.Level)
            {
                case TraceLevel.Error:
                    return ErrorColor(traceEvent.Message);
                case TraceLevel.Warning:
                    return traceEvent.Message.Yellow();
                case TraceLevel.Info:
                    return AdditionalInfoColor(traceEvent.Message);
                case TraceLevel.Verbose:
                    return VerboseColor(traceEvent.Message);
                default:
                    return traceEvent.Message.White();
            }
        }
    }
}
