// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using NCli;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Run the specified function locally", Usage = "<functionName>")]
    internal class RunVerb : BaseVerb
    {
        [Option(0)]
        public string FunctionName { get; set; }

        [Option('t', "timeout", DefaultValue = 15, HelpText = "Time to wait until Functions Server is ready in Seconds")]
        public int Timeout { get; set; }

        [Option('c', "content", HelpText = "In line content to use")]
        public string Content { get; set; }

        [Option('f', "file", HelpText = "File name to use as content")]
        public string FileName { get; set; }

        private readonly IFunctionsLocalServer _scriptServer;

        public RunVerb(IFunctionsLocalServer scriptServer)
        {
            _scriptServer = scriptServer;
        }

        public override async Task RunAsync()
        {
            using (var client = await _scriptServer.ConnectAsync(TimeSpan.FromSeconds(Timeout)))
            {
                var invocation = string.IsNullOrEmpty(FileName)
                    ? Content
                    : await FileSystemHelpers.ReadAllTextFromFileAsync(FileName);

                invocation = invocation ?? string.Empty;

                var adminInvocation = JsonConvert.SerializeObject(new FunctionInvocation { Input = invocation, WaitForCompletion = true });

                var response = await IsHttpFunction()
                    ? await client.PostAsync($"api/{FunctionName}", new StringContent(invocation, Encoding.UTF8, invocation.IsJson() ? "application/json" : "plain/text"))
                    : await client.PostAsync($"admin/functions/{FunctionName}", new StringContent(adminInvocation, Encoding.UTF8, "application/json"));

                ColoredConsole.WriteLine($"{TitleColor($"Response Status Code:")} {response.StatusCode}");
                var contentTask = response?.Content?.ReadAsStringAsync();
                if (contentTask != null)
                {
                    ColoredConsole.WriteLine(await contentTask);
                }

                ColoredConsole
                    .WriteLine()
                    .WriteLine()
                    .WriteLine($"{TitleColor("Tip:")} run {ExampleColor("func list functionapps")} to list function apps in your azure subscription.")
                    .WriteLine($"{TitleColor("Tip:")} run {ExampleColor("func list storageaccounts")} to list storage accounts in your azure subscription.")
                    .WriteLine($"{TitleColor("Tip:")} run {ExampleColor("func list secrets")} to list secrets locally on your machine.");
            }
        }

        private async Task<bool> IsHttpFunction()
        {
            try
            {
                var config = JsonConvert.DeserializeObject<JObject>(await FileSystemHelpers.ReadAllTextFromFileAsync(Path.Combine(Environment.CurrentDirectory, FunctionName, "function.json")));
                if (config?["bindings"]?.FirstOrDefault(e => e["type"].ToString() == "httpTrigger") != null)
                {
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                ColoredConsole.Error.Write(ErrorColor(e.ToString()));
                return false;
            }
        }
    }
}
