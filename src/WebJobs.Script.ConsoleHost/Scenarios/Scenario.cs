// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public abstract class Scenario
    {
        public readonly ITracer Tracer;
        public Scenario(ITracer tracer)
        {
            Tracer = tracer;
        }

        public abstract Task Run();
    }
}
