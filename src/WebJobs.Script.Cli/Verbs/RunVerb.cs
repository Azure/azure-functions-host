// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using NCli;
using Newtonsoft.Json;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Helpers;
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

        [Option('d', "debug", HelpText = "Attach a debugger to the host process before running the function.")]
        public bool Debug { get; set; }

        private readonly IFunctionsLocalServer _scriptServer;

        public RunVerb(IFunctionsLocalServer scriptServer)
        {
            _scriptServer = scriptServer;
        }

        public override async Task RunAsync()
        {
            using (var client = await _scriptServer.ConnectAsync(TimeSpan.FromSeconds(Timeout)))
            {
                var hostStatusResponse = await client.GetAsync("admin/host/status");
                var functionStatusResponse = await client.GetAsync($"admin/functions/{FunctionName}/status");

                if (!hostStatusResponse.IsSuccessStatusCode)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Error calling the functions host: {hostStatusResponse.StatusCode}"));
                    return;
                }
                else if (!functionStatusResponse.IsSuccessStatusCode)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Error calling function {FunctionName}: {functionStatusResponse.StatusCode}"));
                    return;
                }
                

                var functionStatus = await functionStatusResponse.Content.ReadAsAsync<FunctionStatus>();
                var hostStatus = await hostStatusResponse.Content.ReadAsAsync<HostStatus>();
                Func<IEnumerable<string>, string, bool> printError = (errors, title) =>
                {
                    if (errors?.Any() == true)
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor(title));

                        foreach (var error in errors)
                        {
                            ColoredConsole
                                .Error
                                .WriteLine(ErrorColor($"\t{error}"));
                        }
                        return true;
                    }
                    return false;
                };

                if (printError(hostStatus.Errors, "The function host has the following errors:") ||
                    printError(hostStatus.Errors, $"Function {FunctionName} has the following errors:"))
                {
                    return;
                }

                if (Debug)
                {
                    var scriptType = functionStatus.Metadata?.ScriptType;
                    if (scriptType == null)
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor("Unable to read function config"));
                        return;
                    }
                    else if (scriptType != null && scriptType != ScriptType.CSharp)
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor($"Only C# functions are supported for debugging at the moment."));
                        return;
                    }

                    if (scriptType == ScriptType.CSharp && !hostStatus.IsDebuggerAttached)
                    {
                        ColoredConsole
                            .WriteLine("Debugger launching...")
                            .WriteLine("Setup your break points, and hit continue!");
                        await DebuggerHelper.AttachManagedAsync(client);
                    }
                    //else if (scriptType == ScriptType.Javascript)
                    //{
                    //    var nodeDebugger = await DebuggerHelper.TryAttachNodeAsync(hostStatus.ProcessId);
                    //    if (nodeDebugger == NodeDebuggerStatus.Error)
                    //    {
                    //        ColoredConsole
                    //            .Error
                    //            .WriteLine(ErrorColor("Unable to configure node debugger."));
                    //        return;
                    //    }
                    //    else if (nodeDebugger == NodeDebuggerStatus.Created)
                    //    {
                    //        ColoredConsole
                    //        .Write("launch.json configured. Setup your break points, and press any key to continue!");
                    //        Console.ReadKey();
                    //    }
                    //}
                }

                var invocation = string.IsNullOrEmpty(FileName)
                    ? Content
                    : await FileSystemHelpers.ReadAllTextFromFileAsync(FileName);

                invocation = invocation ?? string.Empty;

                var adminInvocation = JsonConvert.SerializeObject(new FunctionInvocation { Input = invocation, WaitForCompletion = true });

                var response = functionStatus.IsHttpFunction()
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
    }
}
