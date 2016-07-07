// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Interfaces;
using WebJobs.Script.Cli.NativeMethods;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli
{
    internal class FunctionsLocalServer : IFunctionsLocalServer
    {
        private const int Port = 7071;
        private readonly ISettings _settings;

        public FunctionsLocalServer(ISettings settings)
        {
            _settings = settings;
        }

        public async Task<HttpClient> ConnectAsync(TimeSpan timeout)
        {
            var server = await DiscoverServer();
            var startTime = DateTime.UtcNow;
            while (!await server.IsServerRunningAsync() &&
                startTime.Add(timeout) > DateTime.UtcNow)
            {
                await Task.Delay(500);
            }
            return new HttpClient() { BaseAddress = server };
        }

        private async Task<Uri> DiscoverServer(int iteration = 0)
        {
            var server = new Uri($"http://localhost:{Port + iteration}");

            if (!await server.IsServerRunningAsync())
            {
                // create the server
                if (_settings.DisplayLaunchingRunServerWarning)
                {
                    ColoredConsole
                        .WriteLine()
                        .WriteLine("We need to launch a server that will host and run your functions.")
                        .WriteLine("The server will auto load any changes you make to the function.");
                    string answer = null;
                    do
                    {
                        ColoredConsole
                            .Write(QuestionColor("Do you want to always display this warning before launching a new server [yes/no]? [yes] "));

                        answer = Console.ReadLine()?.Trim()?.ToLowerInvariant();
                        answer = string.IsNullOrEmpty(answer) ? "yes" : answer;
                    } while (answer != "yes" && answer != "no");
                    _settings.DisplayLaunchingRunServerWarning = answer == "yes" ? true : false;
                }

                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                var exe = new Executable("cmd.exe", $"/c start {exeName} web -n -p {Port + iteration}", streamOutput: false, shareConsole: true);
                exe.RunAsync().Ignore();
                await Task.Delay(500);
                ConsoleNativeMethods.GetFocusBack();
                return server;
            }
            else
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(new Uri(server, "admin/host/status"));
                    response.EnsureSuccessStatusCode();

                    var hostStatus = await response.Content.ReadAsAsync<HostStatus>();
                    if (!hostStatus.WebHostSettings.ScriptPath.Equals(Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        return await DiscoverServer(iteration + 1);
                    }
                    else
                    {
                        return server;
                    }
                }
            }
        }
    }
}
