// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// This <see cref="TraceWriter"/> delegates to an inner user <see cref="TraceWriter"/> (from <see cref="JobHostConfiguration.Trace"/>,
    /// as well as a Console <see cref="TextWriter"/>.
    /// </summary>
    internal class ConsoleTraceWriter : CompositeTraceWriter
    {
        public ConsoleTraceWriter(TraceWriter userTraceWriter, TextWriter console)
            : base(userTraceWriter, console)
        {
        }

        protected override void InvokeTextWriter(TraceLevel level, string source, string message, Exception ex)
        {
            // We only want to log Info/Verbose logs from an internal SDK source
            // to the Console (not user traces, Warnings/Errors, etc.)
            if (TraceSource.IsSdkSource(source) && TraceLevel.Info <= level)
            {
                base.InvokeTextWriter(level, source, message, ex);
            }         
        }
    }
}
