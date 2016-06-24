// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public abstract class Scenario
    {
        private readonly TraceWriter Tracer;
        public Scenario(TraceWriter tracer)
        {
            Tracer = tracer;
        }

        public void TraceInfo(string message)
        {
            Tracer.Info(message, Constants.CliTracingSource);
        }

        public abstract Task Run();
    }
}
