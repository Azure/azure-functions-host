// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script.WebHost;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Helpers;

namespace WebJobs.Script.Cli.Actions.HostActions
{
    [Action(Name = "start", Context = Context.Host)]
    class StartHostAction : BaseAction, IDisposable
    {
        private FileSystemWatcher fsWatcher;
        const int DefaultPort = 7071;
        const int DefaultNodeDebugPort = 5858;

        public int Port { get; set; }

        public int NodeDebugPort { get; set; }

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

            var settings = SelfHostWebHostSettingsFactory.Create(NodeDebugPort);
            Environment.SetEnvironmentVariable("EDGE_NODE_PARAMS", $"--debug={settings.NodeDebugPort}", EnvironmentVariableTarget.Process);

            WebApiConfig.Initialize(config, settings: settings);

            using (var httpServer = new HttpSelfHostServer(config))
            {
                await httpServer.OpenAsync();
                ColoredConsole.WriteLine($"Listening on {baseAddress}");
                ColoredConsole.WriteLine("Hit CTRL-C to exit...");
                await Task.Delay(-1);
                await httpServer.CloseAsync();
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

        private string Setup()
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

            return $"http://localhost:{Port}";
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
