// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// This <see cref="TraceWriter"/> delegates to an inner <see cref="TraceWriter"/> and <see cref="TextWriter"/>.
    /// </summary>
    internal class CompositeTraceWriter : TraceWriter
    {
        private readonly TraceWriter _innerTraceWriter;
        private readonly TextWriter _innerTextWriter;

        public CompositeTraceWriter(TraceWriter traceWriter, TextWriter textWriter)
            : base(TraceLevel.Verbose)
        {
            _innerTraceWriter = traceWriter;
            _innerTextWriter = textWriter;
        }

        public override void Trace(TraceLevel level, string source, string message, Exception ex)
        {
            InvokeTraceWriter(level, source, message, ex);
            InvokeTextWriter(level, source, message, ex);
        }

        protected virtual void InvokeTraceWriter(TraceLevel level, string source, string message, Exception ex)
        {
            // filter based on level before delegating
            if (_innerTraceWriter != null && _innerTraceWriter.Level >= level)
            {
                _innerTraceWriter.Trace(level, source, message, ex);
            }
        }

        protected virtual void InvokeTextWriter(TraceLevel level, string source, string message, Exception ex)
        {
            if (_innerTextWriter != null)
            {
                if (!string.IsNullOrEmpty(message) &&
                     message.EndsWith("\r\n", StringComparison.OrdinalIgnoreCase))
                {
                    // remove any terminating return+line feed, since we're
                    // calling WriteLine below
                    message = message.Substring(0, message.Length - 2);
                }

                _innerTextWriter.WriteLine(message);
                if (ex != null)
                {
                    _innerTextWriter.WriteLine(ex.ToDetails());
                }
            }
        }

        public override void Flush()
        {
            if (_innerTraceWriter != null)
            {
                _innerTraceWriter.Flush();
            }

            if (_innerTextWriter != null)
            {
                _innerTextWriter.Flush();
            }
        }
    }
}
