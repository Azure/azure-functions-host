using System;
using System.Web.Http.Tracing;
using Microsoft.AspNet.WebHooks.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.WebHost.WebHooks
{
    /// <summary>
    /// Custom <see cref="ILogger"/> implementation used for ASP.NET WebHooks SDK integration.
    /// </summary>
    internal class WebHookLogger : ILogger
    {
        private readonly TraceWriter _traceWriter;

        public WebHookLogger(TraceWriter traceWriter)
        {
            _traceWriter = traceWriter;
        }

        public void Log(TraceLevel level, string message, Exception ex)
        {
            // Route all logs coming from the WebHooks SDK as Verbose.
            _traceWriter.Trace(new TraceEvent(System.Diagnostics.TraceLevel.Verbose, message, null, ex));
        }
    }
}