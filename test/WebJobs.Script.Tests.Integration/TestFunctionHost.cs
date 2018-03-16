using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestFunctionHost : IDisposable
    {
        private readonly WebHostSettings _hostSettings;
        private readonly TestServer _testServer;
        private readonly string _appRoot;
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public TestFunctionHost(string appRoot)
        {
            _appRoot = appRoot;

            _hostSettings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = _appRoot,
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Environment.CurrentDirectory // not used
            };

            _testServer = new TestServer(
                AspNetCore.WebHost.CreateDefaultBuilder()
                .UseStartup<Startup>()
                .ConfigureServices(services =>
                {
                    services.Replace(new ServiceDescriptor(typeof(WebHostSettings), _hostSettings));
                    services.Replace(new ServiceDescriptor(typeof(ILoggerProviderFactory), new TestLoggerProviderFactory(_loggerProvider, includeDefaultLoggerProviders: false)));
                    services.Replace(new ServiceDescriptor(typeof(ISecretManager), new TestSecretManager()));
                }));

            HttpClient = _testServer.CreateClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");
        }

        public ScriptHostConfiguration ScriptConfig => _testServer.Host.Services.GetService<WebHostResolver>().GetScriptHostConfiguration(_hostSettings);

        public ISecretManager SecretManager => _testServer.Host.Services.GetService<ISecretManager>();

        public string LogPath => _hostSettings.LogPath;

        public string ScriptPath => _hostSettings.ScriptPath;

        public async Task<string> GetMasterKeyAsync()
        {
            HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();
            return secrets.MasterKey;
        }

        public async Task<string> GetFunctionSecretAsync(string functionName)
        {
            var secrets = await SecretManager.GetFunctionSecretsAsync(functionName);
            return secrets.First().Value;
        }

        public HttpClient HttpClient { get; private set; }

        public async Task StartAsync()
        {
            bool running = false;
            while (!running)
            {
                running = await IsHostRunning(HttpClient);

                if (!running)
                {
                    await Task.Delay(500);
                }
            }
        }

        public void SetNugetPackageSources(params string[] sources)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;

            using (XmlWriter writer = XmlWriter.Create(Path.Combine(_appRoot, "nuget.config"), settings))
            {
                writer.WriteStartElement("configuration");
                writer.WriteStartElement("packageSources");
                for (int i = 0; i < sources.Length; i++)
                {
                    writer.WriteStartElement("add");
                    writer.WriteAttributeString("key", $"source{i}");
                    writer.WriteAttributeString("value", sources[i]);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        public IEnumerable<LogMessage> GetLogMessages() => _loggerProvider.GetAllLogMessages();

        public IEnumerable<LogMessage> GetLogMessages(string category) => GetLogMessages().Where(p => p.Category == category);

        public string GetLog() => string.Join(Environment.NewLine, GetLogMessages());

        public void ClearLogMessages() => _loggerProvider.ClearAllLogMessages();

        public async Task BeginFunctionAsync(string functionName, JToken payload)
        {
            JObject wrappedPayload = new JObject
            {
                { "input", payload.ToString() }
            };

            HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();
            string uri = $"admin/functions/{functionName}?code={secrets.MasterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent(wrappedPayload.ToString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task InstallBindingExtension(string packageName, string packageVersion)
        {
            HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();
            string uri = $"admin/host/extensions?code={secrets.MasterKey}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);

            string payload = new JObject
            {
                { "id", packageName },
                {"version", packageVersion }
            }.ToString(Newtonsoft.Json.Formatting.None);

            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await HttpClient.SendAsync(request);
            var jobStatusUri = response.Headers.Location;
            string status = null;
            do
            {
                await Task.Delay(500);
                response = await CheckExtensionInstallStatus(jobStatusUri);
                var jobStatus = await response.Content.ReadAsAsync<JObject>();
                status = jobStatus["status"].ToString();
            } while (status == "Started");

            if (status != "Succeeded")
            {
                throw new InvalidOperationException("Failed to install extension.");
            }
        }

        private async Task<HttpResponseMessage> CheckExtensionInstallStatus(Uri jobLocation)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, jobLocation);
            return await HttpClient.SendAsync(request);
        }

        public async Task<FunctionStatus> GetFunctionStatusAsync(string functionName)
        {
            HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();
            string uri = $"admin/functions/{functionName}/status?code={secrets.MasterKey}";
            HttpResponseMessage response = await HttpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsAsync<FunctionStatus>();
        }

        public async Task<HostStatus> GetHostStatusAsync()
        {
            HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();
            string uri = $"admin/host/status?code={secrets.MasterKey}";
            HttpResponseMessage response = await HttpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsAsync<HostStatus>();
        }

        public void Dispose()
        {
            HttpClient.Dispose();
            _testServer.Dispose();
        }

        private async Task<bool> IsHostRunning(HttpClient client)
        {
            HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();

            // Workaround for https://github.com/Azure/azure-functions-host/issues/2397 as the base URL
            // doesn't currently start the host. 
            // Note: the master key "1234" is from the TestSecretManager.
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"/admin/functions/dummyName/status?code={secrets.MasterKey}"))
            {
                using (HttpResponseMessage response = await client.SendAsync(request))
                {
                    return response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound;
                }
            }
        }
    }
}
