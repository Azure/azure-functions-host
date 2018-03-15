using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public interface IFunctionMonitor
    {
        void Start();
        void Stop();
    }
}
