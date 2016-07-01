// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using CommandLine;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class RunCommand : Command
    {
        [ValueOption(0)]
        public string FunctionName { get; set; }

        [Option('t', "timeout", DefaultValue = 10, HelpText = "")]
        public int Timeout { get; set; }

        [Option('c', "content", HelpText = "")]
        public string Content { get; set; }

        [Option('f', "file", HelpText = "")]
        public string FileName { get; set; }

        public override async Task Run()
        {
            var _scriptServer = new InMemoryScriptHostServer(Tracer);

            var startTime = DateTime.UtcNow;
            while (!_scriptServer.IsHostRunning() &&
                startTime.AddSeconds(Timeout) > DateTime.UtcNow)
            {
                await Task.Delay(200);
            }

            if (_scriptServer.IsHostRunning())
            {
                var invocation = string.IsNullOrEmpty(FileName)
                    ? Content
                    : File.ReadAllText(FileName);
                invocation = invocation ?? string.Empty;

                var response = await _scriptServer.HttpClient.PostAsync($"api/{FunctionName}?block=true", new StringContent(invocation));
                var content = await response?.Content?.ReadAsStringAsync();
                TraceInfo(content);
            }
            else
            {
                TraceInfo($"Host couldn't start in {Timeout} seconds.\n\tConsider extending timeout by settingh -t <seconds, Default: 10>");
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
    }
}
