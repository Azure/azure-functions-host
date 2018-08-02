﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.IO;
using System.Net.Http;
using System.Web.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class ControllerScenarioTestFixture : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private HttpConfiguration _config;

        public ControllerScenarioTestFixture()
        {            
            _config = new HttpConfiguration();
            _settingsManager = ScriptSettingsManager.Instance;

            HostOptions = new ScriptWebHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample"),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), @"FunctionsTests\Secrets")
            };


            var webHostBuilder = new WebHostBuilder()
                .UseStartup<Startup>()
                .ConfigureAppConfiguration(c => c.AddEnvironmentVariables());

            ConfigureWebHostBuilder(webHostBuilder);

            HttpServer = new TestServer(webHostBuilder);

            HttpClient = HttpServer.CreateClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(HttpClient);
        }

        public ScriptWebHostOptions HostOptions { get; private set; }

        public HttpClient HttpClient { get; set; }

        public TestServer HttpServer { get; set; }

        protected virtual void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder)
        {
            webHostBuilder.ConfigureServices(c => c.AddSingleton(HostOptions));
        }

        public void Dispose()
        {
            HttpServer?.Dispose();
            HttpClient?.Dispose();
        }
    }
}