// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// This <see cref="TraceWriter"/> delegates to an inner user <see cref="TraceWriter"/> (from <see cref="JobHostConfiguration.Tracing"/>,
    /// as well as a Console <see cref="TextWriter"/>.
    /// </summary>
    internal class ConsoleTraceWriter : CompositeTraceWriter
    {
        private readonly TraceLevel _consoleTraceLevel;

        public ConsoleTraceWriter(TraceWriter userTraceWriter, TraceLevel consoleTraceLevel, TextWriter console)
            : base(userTraceWriter, console)
        {
            _consoleTraceLevel = consoleTraceLevel;
        }

        protected override void InvokeTextWriter(TraceLevel level, string source, string message, Exception ex)
        {
            if (MapTraceLevel(source, level) <= _consoleTraceLevel)
            {
                // For Errors/Warnings we change the Console color
                // for visibility
                var holdColor = Console.ForegroundColor;
                bool changedColor = false;
                switch (level)
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

                base.InvokeTextWriter(level, source, message, ex);

                if (changedColor)
                {
                    Console.ForegroundColor = holdColor;
                }
            }         
        }

        internal static TraceLevel MapTraceLevel(string source, TraceLevel level)
        {
            // We want to minimize the noise written to console, so we map the actual
            // TraceLevel to our own interpreted Console TraceLevel
            if (source == TraceSource.Host ||
                source == TraceSource.Indexing ||
                (source == TraceSource.Execution && level == TraceLevel.Info))
            {
                return level;
            }
            else
            {
                // All other traces, are treated as Verbose.
                return TraceLevel.Verbose;
            }
        }
    }
}
