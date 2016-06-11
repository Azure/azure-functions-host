// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Cli;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class RunScenario : Scenario
    {
        private readonly RunVerbOptions _options;

        public RunScenario(RunVerbOptions options, ITracer tracer) : base(tracer)
        {
            _options = options;
        }

        public override async Task Run()
        {
            await Tracer.WriteLineAsync(_options.ToString());
            await Tracer.WriteLineAsync(_options.FunctionName);
        }
    }
}
