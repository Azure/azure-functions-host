using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
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
