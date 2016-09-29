// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// This <see cref="TraceWriter"/> delegates to a collection of inner user <see cref="TraceWriter"/>s (from <see cref="JobHostConfiguration.Tracing"/>,
    /// as well as a Console <see cref="TextWriter"/>.
    /// </summary>
    internal class ConsoleTraceWriter : CompositeTraceWriter
    {
        private readonly JobHostTraceConfiguration _traceConfig;

        public ConsoleTraceWriter(JobHostTraceConfiguration traceConfig, TextWriter console)
            : base(traceConfig.Tracers, console)
        {
            _traceConfig = traceConfig;
        }

        protected override void InvokeTextWriter(TraceEvent traceEvent)
        {
            if (traceEvent.Level <= _traceConfig.ConsoleLevel)
            {
                // For Errors/Warnings we change the Console color
                // for visibility
                var holdColor = Console.ForegroundColor;
                bool changedColor = false;
                switch (traceEvent.Level)
                {
                    case TraceLevel.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        changedColor = true;
                        break;
                    case TraceLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        changedColor = true;
                        break;
                }

                base.InvokeTextWriter(traceEvent);

                if (changedColor)
                {
                    Console.ForegroundColor = holdColor;
                }
            }
        }
    }
}
