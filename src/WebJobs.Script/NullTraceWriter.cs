using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NullTraceWriter : TraceWriter
    {
        public NullTraceWriter() : base (TraceLevel.Off)
        {
        }

        public override void Trace(TraceEvent traceEvent)
        {
        }
    }
}
