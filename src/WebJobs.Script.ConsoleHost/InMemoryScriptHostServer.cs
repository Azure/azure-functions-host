// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost
{
    public class InMemoryScriptHostServer
    {
        public HttpClient HttpClient { get; private set; }

        public HttpServer HttpServer { get; private set; }

        public InMemoryScriptHostServer(TraceWriter tracerWriter)
        {
            HttpConfiguration config = new HttpConfiguration();

            var settings = SelfHostWebHostSettingsFactory.Create(tracerWriter);
            WebApiConfig.Register(config, settings);

            HttpServer = new HttpServer(config);
            this.HttpClient = new HttpClient(HttpServer);
            this.HttpClient.BaseAddress = new Uri("https://localhost/");
        }

        public bool IsHostRunning()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, string.Empty);

            HttpResponseMessage response = this.HttpClient.SendAsync(request).Result;
            return response.StatusCode == HttpStatusCode.NoContent;
        }
    }
}
