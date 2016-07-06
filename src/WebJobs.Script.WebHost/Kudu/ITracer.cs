using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public interface ITracer
    {
        TraceLevel TraceLevel { get; }
        IDisposable Step(string message, IDictionary<string, string> attributes);
        void Trace(string message, IDictionary<string, string> attributes);
    }
}
