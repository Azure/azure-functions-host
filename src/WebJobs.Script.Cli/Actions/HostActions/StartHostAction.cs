// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Helpers;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.HostActions
{
    [Action(Name = "start", Context = Context.Host)]
    class StartHostAction : BaseAction, IDisposable
    {
        private FileSystemWatcher fsWatcher;
        const int DefaultPort = 7071;
        const int DefaultNodeDebugPort = 5858;
        const TraceLevel DefaultDebugLevel = TraceLevel.Info;

        public int Port { get; set; }

        public int NodeDebugPort { get; set; }

        public TraceLevel ConsoleTraceLevel { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<int>('p', "port")
                .WithDescription($"Local port to listen on. Default: {DefaultPort}")
                .SetDefault(DefaultPort)
                .Callback(p => Port = p);

            Parser
                .Setup<int>('n', "nodeDebugPort")
                .WithDescription($"Port for node debugger to use. Default: {DefaultNodeDebugPort}")
                .SetDefault(DefaultNodeDebugPort)
                .Callback(p => NodeDebugPort = p);

            Parser
                .Setup<TraceLevel>('d', "debugLevel")
                .WithDescription($"Console trace level (off, verbose, info, warning or error). Default: {DefaultDebugLevel}")
                .SetDefault(DefaultDebugLevel)
                .Callback(p => ConsoleTraceLevel = p);

            return Parser.Parse(args);
        }

        public override async Task RunAsync()
        {
            ReadSecrets();
            var baseAddress = Setup();

            var config = new HttpSelfHostConfiguration(baseAddress)
            {
                IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always,
                TransferMode = TransferMode.Streamed
            };

            var settings = SelfHostWebHostSettingsFactory.Create(NodeDebugPort, ConsoleTraceLevel);

            Environment.SetEnvironmentVariable("EDGE_NODE_PARAMS", $"--debug={settings.NodeDebugPort}", EnvironmentVariableTarget.Process);

            WebApiConfig.Initialize(config, settings: settings);
            using (var httpServer = new HttpSelfHostServer(config))
            {
                await httpServer.OpenAsync();
                ColoredConsole.WriteLine($"Listening on {baseAddress}");
                ColoredConsole.WriteLine("Hit CTRL-C to exit...");
                await PostHostStartActions(baseAddress);
                DisableCoreLogging(config);
                await Task.Delay(-1);
                await httpServer.CloseAsync();
            }
        }

        private static void DisableCoreLogging(HttpSelfHostConfiguration config)
        {
            WebScriptHostManager hostManager = config.DependencyResolver.GetService<WebScriptHostManager>();

            if (hostManager != null)
            {
                hostManager.Instance.ScriptConfig.HostConfig.Tracing.ConsoleLevel = TraceLevel.Off;
            }
        }

        private async Task PostHostStartActions(Uri server)
        {
            try
            {
                while (!await server.IsServerRunningAsync())
                {
                    await Task.Delay(500);
                }

                using (var client = new HttpClient() { BaseAddress = server })
                {
                    var functionsResponse = await client.GetAsync("admin/functions");
                    var hostConfigResponse = await client.GetAsync("admin/functions/config");
                    var functions = await functionsResponse.Content.ReadAsAsync<FunctionEnvelope[]>();
                    var httpFunctions = functions.Where(f => f.Config?["bindings"]?.Any(b => b["type"].ToString() == "httpTrigger") == true);
                    var hostConfig = await hostConfigResponse.Content.ReadAsAsync<JObject>();
                    foreach (var function in httpFunctions)
                    {
                        var httpRoute = function.Config["bindings"]?.FirstOrDefault(b => b["type"].ToString() == "httpTrigger")?["route"]?.ToString();
                        httpRoute = httpRoute ?? function.Name;
                        var hostRoutePrefix = hostConfig?["http"]?["routePrefix"]?.ToString() ?? "api/";
                        hostRoutePrefix = string.IsNullOrEmpty(hostRoutePrefix) || hostRoutePrefix.EndsWith("/")
                            ? hostRoutePrefix
                            : $"{hostRoutePrefix}/";
                        ColoredConsole.WriteLine($"{TitleColor($"Http Function {function.Name}:")} {server.ToString()}{hostRoutePrefix}{httpRoute}");
                    }
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Unable to retrieve functions list: {ex.Message}"));
            }
        }

        private void ReadSecrets()
        {
            try
            {
                var secretsManager = new SecretsManager();

                foreach (var pair in secretsManager.GetSecrets())
                {
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.Process);
                }
            }
            catch { }

            fsWatcher = new FileSystemWatcher(Environment.CurrentDirectory, SecretsManager.AppSettingsFileName);
            fsWatcher.Changed += (s, e) =>
            {
                Environment.Exit(ExitCodes.Success);
            };
            fsWatcher.EnableRaisingEvents = true;
        }

        private Uri Setup()
        {
            if (!SecurityHelpers.IsUrlAclConfigured("http", Port))
            {
                string errors;
                // TODONOW
                if (!ConsoleApp.RelaunchSelfElevated(new InternalUseAction { Port = Port, Action = InternalAction.SetupUrlAcl }, out errors))
                {
                    ColoredConsole.WriteLine("Error: " + errors);
                    Environment.Exit(ExitCodes.GeneralError);
                }
            }

            return new Uri($"http://localhost:{Port}");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                fsWatcher.Dispose();
            }
        }
    }
}
