// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.IO;
using System.Net.Http;
using System.Web.Http;
using Autofac;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class ControllerScenarioTestFixture : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private HttpConfiguration _config;

        public ControllerScenarioTestFixture()
            : this(isAuthDisabled: false)
        {
        }

        public ControllerScenarioTestFixture(bool isAuthDisabled)
        {
            _config = new HttpConfiguration();
            _settingsManager = ScriptSettingsManager.Instance;

            HostSettings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\sample"),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\src\WebJobs.Script.WebHost\App_Data\Secrets"),
                IsAuthDisabled = isAuthDisabled
            };

            HttpServer = new HttpServer(_config);
            HttpClient = new HttpClient(HttpServer);
            HttpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(HttpClient);
        }

        public WebHostSettings HostSettings { get; private set; }

        public HttpClient HttpClient { get; set; }

        public HttpServer HttpServer { get; set; }

        protected virtual void RegisterDependencies(ContainerBuilder builder, WebHostSettings settings)
        {
        }

        public void Dispose()
        {
            HttpServer?.Dispose();
            HttpClient?.Dispose();
        }
    }
}