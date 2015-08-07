// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Base class for trace writers used by <see cref="JobHost"/>. 
    /// See <see cref="JobHostConfiguration.Tracing"/> for details.
    /// </summary>
    public abstract class TraceWriter
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="level">The <see cref="TraceLevel"/> used to filter traces.</param>
        protected TraceWriter(TraceLevel level)
        {
            Level = level;
        }

        /// <summary>
        /// Gets or sets the <see cref="TraceLevel"/> filter used to filter traces.
        /// Only trace entries with a <see cref="TraceLevel"/> less than or equal to
        /// this level will be logged.
        /// <remarks>
        /// Level filtering will be done externally by the <see cref="JobHost"/>, so
        /// does not need to be done by this class.
        /// </remarks>
        /// </summary>
        public TraceLevel Level { get; set; }

        /// <summary>
        /// Writes a trace entry.
        /// </summary>
        /// <param name="level">The <see cref="TraceLevel"/> for the trace entry</param>
        /// <param name="source">Optional source of the message.</param>
        /// <param name="message">The trace message.</param>
        /// <param name="ex">Optional <see cref="Exception"/> (if an error is being traced).</param>
        public abstract void Trace(TraceLevel level, string source, string message, Exception ex);

        /// <summary>
        /// Writes a <see cref="TraceLevel.Info"/> level trace entry.
        /// </summary>
        /// <param name="message">The trace message.</param>
        /// <param name="source">The source of the message.</param>
        public void Info(string message, string source = null)
        {
            Trace(TraceLevel.Info, source, message, null);
        }

        /// <summary>
        /// Writes a <see cref="TraceLevel.Warning"/> level trace entry.
        /// </summary>
        /// <param name="message">The trace message.</param>
        /// <param name="source">The source of the message.</param>
        public void Warning(string message, string source = null)
        {
            Trace(TraceLevel.Warning, source, message, null);
        }

        /// <summary>
        /// Writes a <see cref="TraceLevel.Error"/> level trace entry.
        /// </summary>
        /// <param name="message">The trace message.</param>
        /// <param name="ex">The optional <see cref="Exception"/> for the error.</param>
        /// <param name="source">The source of the message.</param>
        public void Error(string message, Exception ex = null, string source = null)
        {
            Trace(TraceLevel.Error, source, message, ex);
        }

        /// <summary>
        /// Flush any buffered trace entries.
        /// </summary>
        public virtual void Flush()
        {
        }
    }
}
