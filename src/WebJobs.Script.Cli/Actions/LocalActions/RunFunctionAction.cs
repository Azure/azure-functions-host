using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Helpers;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "run", Context = Context.Function, HelpText = "Run a function directly")]
    [Action(Name = "run", HelpText = "Run a function directly")]
    class RunFunctionAction : BaseAction
    {
        private readonly IFunctionsLocalServer _scriptServer;

        public string FunctionName { get; set; }
        public int Timeout { get; set; }
        public string Content { get; set; }
        public string FileName { get; set; }
        public bool Debug { get; set; }

        public RunFunctionAction(IFunctionsLocalServer scriptServer)
        {
            _scriptServer = scriptServer;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                FunctionName = args.First();
                Parser
                    .Setup<int>('t', "timeout")
                    .WithDescription("Time to wait until Functions Server is ready in Seconds")
                    .SetDefault(15)
                    .Callback(t => Timeout = t);
                Parser
                    .Setup<string>('c', "content")
                    .WithDescription("In line content to use")
                    .Callback(c => Content = c);
                Parser
                    .Setup<string>('f', "file")
                    .WithDescription("File name to use as content")
                    .Callback(f => FileName = f);
                Parser
                    .Setup<bool>('d', "debug")
                    .WithDescription("Attach a debugger to the host process before running the function.")
                    .Callback(d => Debug = d);
                return Parser.Parse(args);
            }
            else
            {
                throw new ArgumentException("Must specify function to run");
            }
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
                    else if (scriptType != null && scriptType != ScriptType.CSharp && scriptType != ScriptType.Javascript)
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor($"Only C# and Javascript functions are supported for debugging at the moment."));
                        return;
                    }

                    if (scriptType == ScriptType.CSharp && !hostStatus.IsDebuggerAttached)
                    {
                        ColoredConsole
                            .WriteLine("Debugger launching...")
                            .WriteLine("Setup your break points, and hit continue!");
                        await DebuggerHelper.AttachManagedAsync(client);
                    }
                    else if (scriptType == ScriptType.Javascript)
                    {
                        var nodeDebugger = await DebuggerHelper.TryAttachNodeAsync(client);
                        if (nodeDebugger == NodeDebuggerStatus.Error)
                        {
                            ColoredConsole
                                .Error
                                .WriteLine(ErrorColor("Unable to configure node debugger. Check your launch.json."));
                            return;
                        }
                        else if (nodeDebugger == NodeDebuggerStatus.Created || nodeDebugger == NodeDebuggerStatus.AlreadyCreated)
                        {
                            ColoredConsole
                            .Write("launch.json configured. Setup your break points, launch debugger (F5), and press any key to continue...");
                            Console.ReadKey();
                        }
                    }
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
                var contentTask = response.Content?.ReadAsStringAsync();
                if (contentTask != null)
                {
                    var content = await contentTask;
                    if (!response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var exception = JsonConvert.DeserializeObject<JObject>(content);
                            if (exception?["InnerException"]?["ExceptionMessage"]?.ToString() == "Script compilation failed.")
                            {
                                ColoredConsole.Error.WriteLine(ErrorColor("Script compilation failed."));
                                return;
                            }
                        }
                        catch { }
                    }
                    ColoredConsole.WriteLine(await contentTask);
                }
            }
        }
    }
}
