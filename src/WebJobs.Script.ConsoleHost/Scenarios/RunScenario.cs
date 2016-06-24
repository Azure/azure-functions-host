// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Cli;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class RunScenario : Scenario
    {
        private readonly RunVerbOptions _options;
        private readonly InMemoryScriptHostServer _scriptServer;

        public RunScenario(RunVerbOptions options, TraceWriter tracer) : base(tracer)
        {
            _options = options;
            _scriptServer = new InMemoryScriptHostServer(tracer);
        }

        public override async Task Run()
        {

            var startTime = DateTime.UtcNow;
            while (!_scriptServer.IsHostRunning() &&
                startTime.AddSeconds(_options.Timeout) > DateTime.UtcNow)
            {
                await Task.Delay(200);
            }

            if (_scriptServer.IsHostRunning())
            {
                await InvokeFunction();
            }
            else
            {
                TraceInfo($"Host couldn't start in {_options.Timeout} seconds.\n\tConsider extending timeout by settingh -t <seconds, Default: 10>");
                var errorsResponse = await _scriptServer.HttpClient.GetAsync("admin/host/status");
                var errors = await errorsResponse.Content.ReadAsAsync<HostStatus>();
                if (errors?.Errors?.Count() > 0)
                {
                    TraceInfo("Host Errors: ");
                    foreach (var error in errors.Errors)
                    {
                        TraceInfo(error);
                    }
                }
            }
        }

        private async Task InvokeFunction()
        {
            var invocation = string.IsNullOrEmpty(_options.FileName)
                ? _options.Content
                : File.ReadAllText(_options.FileName);
            invocation = invocation ?? string.Empty;

            var response = await _scriptServer.HttpClient.PostAsync($"api/{_options.FunctionName}?block=true", new StringContent(invocation));
            var content = await response?.Content?.ReadAsStringAsync();
            TraceInfo(content);
        }
    }
}
