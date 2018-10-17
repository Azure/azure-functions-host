// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestFunctionHost : IDisposable
    {
        private readonly ScriptApplicationHostOptions _hostOptions;
        private readonly TestServer _testServer;
        private readonly string _appRoot;
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly WebJobsScriptHostService _hostService;

        public TestFunctionHost(string scriptPath, string logPath,
            Action<IWebJobsBuilder> configureJobHost = null,
            Action<IConfigurationBuilder> configureAppConfiguration = null,
            Action<IServiceCollection> configureServices = null)
        {
            _appRoot = scriptPath;

            _hostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = _appRoot,
                LogPath = logPath,
                SecretsPath = Environment.CurrentDirectory, // not used
                HasParentScope = true
            };

            var optionsMonitor = TestHelpers.CreateOptionsMonitor(_hostOptions);
            var builder = new WebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(_loggerProvider);
                })
                .ConfigureServices(services =>
                  {
                      services.Replace(new ServiceDescriptor(typeof(ISecretManagerProvider), new TestSecretManagerProvider(new TestSecretManager())));
                      services.Replace(ServiceDescriptor.Singleton<IServiceProviderFactory<IServiceCollection>>(new WebHostServiceProviderFactory()));
                      services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), new OptionsWrapper<ScriptApplicationHostOptions>(_hostOptions)));
                      services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), optionsMonitor));

                      services.AddSingleton<IConfigureBuilder<IConfigurationBuilder>>(_ => new DelegatedConfigureBuilder<IConfigurationBuilder>(c =>
                      {
                          c.AddTestSettings();
                          configureAppConfiguration?.Invoke(c);
                      }));

                      configureServices?.Invoke(services);
                  })
                  .AddScriptHostBuilder(webJobsBuilder =>
                  {
                      webJobsBuilder.Services.AddLogging(loggingBuilder =>
                      {
                          loggingBuilder.AddProvider(_loggerProvider);
                          loggingBuilder.AddFilter<TestLoggerProvider>(_ => true);
                      });

                      webJobsBuilder.AddAzureStorage();

                      configureJobHost?.Invoke(webJobsBuilder);
                  })
                  .UseStartup<Startup>();

            _testServer = new TestServer(builder);

            HttpClient = new HttpClient(new UpdateContentLengthHandler(_testServer.CreateHandler()));
            HttpClient.BaseAddress = new Uri("https://localhost/");

            var manager = _testServer.Host.Services.GetService<IScriptHostManager>();
            _hostService = manager as WebJobsScriptHostService;

            StartAsync().GetAwaiter().GetResult();
        }

        public IServiceProvider JobHostServices => _hostService.Services;

        public ScriptJobHostOptions ScriptOptions => JobHostServices.GetService<IOptions<ScriptJobHostOptions>>().Value;

        public ISecretManager SecretManager => _testServer.Host.Services.GetService<ISecretManagerProvider>().Current;

        public string LogPath => _hostOptions.LogPath;

        public string ScriptPath => _hostOptions.ScriptPath;

        public HttpClient HttpClient { get; private set; }

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

        private async Task StartAsync()
        {
            bool running = false;
            while (!running)
            {
                running = await IsHostStarted(HttpClient);

                if (!running)
                {
                    await Task.Delay(50);
                }
            }
        }

        public void SetNugetPackageSources(params string[] sources)
        {
            WriteNugetPackageSources(_appRoot, sources);
        }

        public static void WriteNugetPackageSources(string appRoot, params string[] sources)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;

            using (XmlWriter writer = XmlWriter.Create(Path.Combine(appRoot, "nuget.config"), settings))
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

        public IList<LogMessage> GetLogMessages() => _loggerProvider.GetAllLogMessages();

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

        private async Task<bool> IsHostStarted(HttpClient client)
        {
            HostStatus status = await GetHostStatusAsync();
            return status.State == $"{ScriptHostState.Running}" || status.State == $"{ScriptHostState.Error}";
        }

        private class UpdateContentLengthHandler : DelegatingHandler
        {
            public UpdateContentLengthHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            {
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Force reading the content-length to ensure the header is populated.
                if (request.Content != null)
                {
                    Trace.Write(request.Content.Headers.ContentLength);
                }

                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
