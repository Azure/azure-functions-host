// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>
    /// The <see cref="TraceWriter"/> passed to function instances. This class adds function instance details
    /// to all trace events.
    /// </summary>
    internal class FunctionInstanceTraceWriter : TraceWriter
    {
        private TraceWriter _innerWriter;
        private IFunctionInstance _instance;
        private Guid _hostInstanceId;

        public FunctionInstanceTraceWriter(IFunctionInstance instance, Guid hostInstanceId, TraceWriter innerWriter, TraceLevel level)
            : base(level)
        {
            _innerWriter = innerWriter;
            _instance = instance;
            _hostInstanceId = hostInstanceId;
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (traceEvent == null)
            {
                throw new ArgumentNullException("traceEvent");
            }

            traceEvent.AddFunctionInstanceDetails(_hostInstanceId, _instance.FunctionDescriptor, _instance.Id);

            _innerWriter.Trace(traceEvent);
        }
    }
}
