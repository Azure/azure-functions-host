// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using WebJobs.Script.ConsoleHost.Common;
using WebJobs.Script.ConsoleHost.Helpers;
using CommandLine;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class WebScenario : Scenario
    {
        [Option('p', "port", DefaultValue = 6061, HelpText = "Local port to listen on")]
        public int Port { get; set; }

        [Option('c', "cert", HelpText = "Path for the cert to use. If not supecified, will auto-generate a cert.")]
        public string CertPath { get; set; }

        [Option('k', "skipCertSetup", DefaultValue = false, HelpText = "Automatically add the cert to the trusted store.")]
        public bool SkipCertSetup { get; set; }

        public override async Task Run()
        {
            Setup();

            var baseAddress = $"https://localhost:{Port}";

            var config = new HttpSelfHostConfiguration(baseAddress)
            {
                IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always,
                TransferMode = TransferMode.Streamed
            };

            WebHostSettings settings = SelfHostWebHostSettingsFactory.Create();

            WebApiConfig.Register(config, settings);

            using (var httpServer = new HttpSelfHostServer(config))
            {
                await httpServer.OpenAsync();
                TraceInfo($"Listening on {baseAddress}");
                TraceInfo("Hit CTRL-C to exit...");
                await Task.Delay(-1);
                await httpServer.CloseAsync();
            }
        }

        private void Setup()
        {
            if (SkipCertSetup)
            {
                TraceInfo($"Skipping cert checks. Assuming SSL is setup for https://localhost:{Port}");
            }
            else
            {
                if (!SecurityHelpers.IsUrlAclConfigured(Port) ||
                    !SecurityHelpers.IsSSLConfigured(Port))
                {
                    string errors;
                    if (!SecurityHelpers.TryElevateAndSetupCerts(CertPath, Port, out errors))
                    {
                        TraceInfo("Error: " + errors);
                        Environment.Exit(ExitCodes.GeneralError);
                    }
                }
            }
        }
    }
}
