// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Autofac;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class ControllerScenarioTestFixture
    {
        private HttpConfiguration _config;

        public ControllerScenarioTestFixture()
        {
            _config = new HttpConfiguration();

            HostSettings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\sample"),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\src\WebJobs.Script.WebHost\App_Data\Secrets")
            };

            WebApiConfig.Register(_config, HostSettings, RegisterDependencies);

            HttpServer = new HttpServer(_config);
            this.HttpClient = new HttpClient(HttpServer);
            this.HttpClient.BaseAddress = new Uri("https://localhost/");

            WaitForHost();
        }

        public WebHostSettings HostSettings { get; private set; }

        public HttpClient HttpClient { get; set; }

        public HttpServer HttpServer { get; set; }

        private void WaitForHost()
        {
            TestHelpers.Await(() =>
            {
                return IsHostRunning();
            }).Wait();
        }

        private bool IsHostRunning()
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty))
            {
                using (HttpResponseMessage response = this.HttpClient.SendAsync(request).Result)
                {
                    return response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK;
                }
            }
        }

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
