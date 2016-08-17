// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost;
using NCli;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Helpers;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Launches a Functions server endpoint locally")]
    internal sealed class WebVerb : BaseVerb, IDisposable
    {
        private FileSystemWatcher fsWatcher;

        [Option('p', "port", DefaultValue = 6061, HelpText = "Local port to listen on")]
        public int Port { get; set; }

        [Option('c', "cert", HelpText = "Path for the cert to use. If not specified, will auto-generate a cert")]
        public string CertPath { get; set; }

        [Option('k', "skipCertSetup", DefaultValue = false, HelpText = "Automatically add the cert to the trusted store")]
        public bool SkipCertSetup { get; set; }

        [Option('n', "nossl", DefaultValue = false, HelpText = "Don't use https")]
        public bool NoSsl { get; set; }

        public WebVerb(ITipsManager tipsManager)
            : base(tipsManager)
        {
            ConfigureDefaultEnvironmentVariables();
            ReadSecrets();
        }

        private static void ConfigureDefaultEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("EDGE_NODE_PARAMS", "--debug", EnvironmentVariableTarget.Process);
        }

        public override async Task RunAsync()
        {
            var baseAddress = Setup();

            var config = new HttpSelfHostConfiguration(baseAddress)
            {
                IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always,
                TransferMode = TransferMode.Streamed
            };

            var settings = SelfHostWebHostSettingsFactory.Create();

            WebApiConfig.Register(config, settings);

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

            fsWatcher = new FileSystemWatcher(Environment.CurrentDirectory, SecretsManager.SecretsFilePath);
            fsWatcher.Changed += (s, e) =>
            {
                Environment.Exit(ExitCodes.Success);
            };
            fsWatcher.EnableRaisingEvents = true;
        }

        private string Setup()
        {
            var protocol = NoSsl ? "http" : "https";

            if (SkipCertSetup)
            {
                ColoredConsole.WriteLine($"Skipping cert checks. Assuming SSL is setup for {protocol}://localhost:{Port}");
            }
            else
            {
                if (NoSsl)
                {
                    if (!SecurityHelpers.IsUrlAclConfigured(protocol, Port))
                    {
                        string errors;
                        if (!Program.RelaunchSelfElevated($"cert -p {Port} -k", out errors))
                        {
                            ColoredConsole.WriteLine("Error: " + errors);
                            Environment.Exit(ExitCodes.GeneralError);
                        }
                    }
                }
                else
                {
                    if (!SecurityHelpers.IsUrlAclConfigured(protocol, Port) ||
                         !SecurityHelpers.IsSSLConfigured(Port))
                    {
                        string errors;
                        if (!SecurityHelpers.TryElevateAndSetupCerts(CertPath, Port, out errors))
                        {
                            ColoredConsole.WriteLine("Error: " + errors);
                            Environment.Exit(ExitCodes.GeneralError);
                        }
                    }
                }
            }

            return $"{protocol}://localhost:{Port}";
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
