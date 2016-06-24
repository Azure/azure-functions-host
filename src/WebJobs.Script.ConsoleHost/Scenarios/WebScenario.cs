// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.SelfHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using WebJobs.Script.ConsoleHost.Cli;
using WebJobs.Script.ConsoleHost.Common;
using WebJobs.Script.ConsoleHost.Helpers;
using Microsoft.Azure.WebJobs.Host;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class WebScenario : Scenario
    {
        private readonly WebVerbOptions _options;

        public WebScenario(WebVerbOptions options, TraceWriter tracer) : base (tracer)
        {
            _options = options;
        }

        public override async Task Run()
        {
            Setup();

            var baseAddress = $"https://localhost:{_options.Port}";

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
            if (_options.SkipCertSetup && !_options.Quiet)
            {
                TraceInfo($"Skipping cert checks. Assuming SSL is setup for https://localhost:{_options.Port}");
            }
            else
            {
                if (!SecurityHelpers.IsUrlAclConfigured(_options.Port) ||
                    !SecurityHelpers.IsSSLConfigured(_options.Port))
                {
                    string errors;
                    if (!SecurityHelpers.TryElevateAndSetupCerts(_options.CertPath, _options.Port, out errors))
                    {
                        TraceInfo("Error: " + errors);
                        Environment.Exit(ExitCodes.GeneralError);
                    }
                }
            }
        }
    }
}
