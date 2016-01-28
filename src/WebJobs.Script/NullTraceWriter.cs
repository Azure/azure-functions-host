using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class NullTraceWriter : TraceWriter
    {
        private static NullTraceWriter _instance = new NullTraceWriter();

        private NullTraceWriter() : base (TraceLevel.Off)
        {
        }

        public static NullTraceWriter Instance
        {
            get
            {
                return _instance;
            }
        }

        public override void Trace(TraceEvent traceEvent)
        {
        }
    }
}
