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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestFunctionHost : IDisposable
    {
        private readonly ScriptApplicationHostOptions _hostOptions;
        private readonly TestServer _testServer;
        private readonly string _appRoot;
        private readonly TestLoggerProvider _webHostLoggerProvider = new TestLoggerProvider();
        private readonly TestLoggerProvider _scriptHostLoggerProvider = new TestLoggerProvider();
        private readonly WebJobsScriptHostService _hostService;

        public TestFunctionHost(string scriptPath, string logPath,
            Action<IServiceCollection> configureWebHostServices = null,
            Action<IWebJobsBuilder> configureScriptHostWebJobsBuilder = null,
            Action<IConfigurationBuilder> configureScriptHostAppConfiguration = null,
            Action<ILoggingBuilder> configureScriptHostLogging = null,
            Action<IServiceCollection> configureScriptHostServices = null)
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

            var builder = new WebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(_webHostLoggerProvider);
                })
                .ConfigureServices(services =>
                  {
                      services.Replace(new ServiceDescriptor(typeof(ISecretManagerProvider), new TestSecretManagerProvider(new TestSecretManager())));
                      services.Replace(ServiceDescriptor.Singleton<IServiceProviderFactory<IServiceCollection>>(new WebHostServiceProviderFactory()));
                      services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), sp =>
                      {
                          _hostOptions.RootServiceProvider = sp;
                          return new OptionsWrapper<ScriptApplicationHostOptions>(_hostOptions);
                      }, ServiceLifetime.Singleton));
                      services.Replace(new ServiceDescriptor(typeof(IOptionsMonitor<ScriptApplicationHostOptions>), sp =>
                      {
                          _hostOptions.RootServiceProvider = sp;
                          return TestHelpers.CreateOptionsMonitor(_hostOptions);
                      }, ServiceLifetime.Singleton));
                      services.Replace(new ServiceDescriptor(typeof(IExtensionBundleManager), new TestExtensionBundleManager()));
                      services.Replace(new ServiceDescriptor(typeof(IFunctionMetadataManager), sp =>
                      {
                          var montior = sp.GetService<IOptionsMonitor<ScriptApplicationHostOptions>>();
                          var scriptManager = sp.GetService<IScriptHostManager>();
                          var loggerFactory = sp.GetService<ILoggerFactory>();

                          return GetMetadataManager(montior, scriptManager, loggerFactory);
                      }, ServiceLifetime.Singleton));

                      // Allows us to configure services as the last step, thereby overriding anything
                      services.AddSingleton(new PostConfigureServices(configureWebHostServices));
                  })
                .ConfigureScriptHostWebJobsBuilder(scriptHostWebJobsBuilder =>
                {
                    scriptHostWebJobsBuilder.AddAzureStorage();
                    configureScriptHostWebJobsBuilder?.Invoke(scriptHostWebJobsBuilder);
                })
                .ConfigureScriptHostAppConfiguration(scriptHostConfigurationBuilder =>
                {
                    scriptHostConfigurationBuilder.AddTestSettings();
                    configureScriptHostAppConfiguration?.Invoke(scriptHostConfigurationBuilder);
                })
                .ConfigureScriptHostLogging(scriptHostLoggingBuilder =>
                {
                    scriptHostLoggingBuilder.AddProvider(_scriptHostLoggerProvider);
                    scriptHostLoggingBuilder.AddFilter<TestLoggerProvider>(_ => true);
                    configureScriptHostLogging?.Invoke(scriptHostLoggingBuilder);
                })
                .ConfigureScriptHostServices(scriptHostServices =>
                {
                    configureScriptHostServices?.Invoke(scriptHostServices);
                })
                .UseStartup<TestStartup>();

            _testServer = new TestServer(builder);

            HttpClient = new HttpClient(new UpdateContentLengthHandler(_testServer.CreateHandler()))
            {
                BaseAddress = new Uri("https://localhost/")
            };

            var manager = _testServer.Host.Services.GetService<IScriptHostManager>();
            _hostService = manager as WebJobsScriptHostService;

            // Wire up StopApplication calls as they behave in hosted scenarios
            var lifetime = WebHostServices.GetService<IApplicationLifetime>();
            lifetime.ApplicationStopping.Register(async () => await _testServer.Host.StopAsync());

            StartAsync().GetAwaiter().GetResult();
        }

        public IServiceProvider JobHostServices => _hostService.Services;

        public IServiceProvider WebHostServices => _testServer.Host.Services;

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

        public async Task RestartAsync(CancellationToken cancellationToken)
        {
            await _hostService.RestartHostAsync(cancellationToken);
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

        /// <summary>
        /// The functions host has two logger providers -- one at the WebHost level and one at the ScriptHost level.
        /// These providers use different LoggerProviders, so it's important to know which one is receiving the logs.
        /// </summary>
        /// <returns>The messages from the ScriptHost LoggerProvider</returns>
        public IList<LogMessage> GetScriptHostLogMessages() => _scriptHostLoggerProvider.GetAllLogMessages();
        public IEnumerable<LogMessage> GetScriptHostLogMessages(string category) => GetScriptHostLogMessages().Where(p => p.Category == category);

        /// <summary>
        /// The functions host has two logger providers -- one at the WebHost level and one at the ScriptHost level.
        /// These providers use different LoggerProviders, so it's important to know which one is receiving the logs.
        /// </summary>
        /// <returns>The messages from the WebHost LoggerProvider</returns>
        public IList<LogMessage> GetWebHostLogMessages() => _webHostLoggerProvider.GetAllLogMessages();

        public string GetLog() => string.Join(Environment.NewLine, GetScriptHostLogMessages().Concat(GetWebHostLogMessages()).OrderBy(m => m.Timestamp));

        public void ClearLogMessages() => _scriptHostLoggerProvider.ClearAllLogMessages();

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

        private class TestStartup
        {
            private Startup _startup;
            private readonly PostConfigureServices _postConfigure;

            public TestStartup(IConfiguration configuration, PostConfigureServices postConfigure)
            {
                _startup = new Startup(configuration);
                _postConfigure = postConfigure;
            }

            public void ConfigureServices(IServiceCollection services)
            {
                _startup.ConfigureServices(services);
                _postConfigure?.ConfigureServices(services);
            }

            public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, AspNetCore.Hosting.IHostingEnvironment env, ILoggerFactory loggerFactory)
            {
                // This middleware is only added when env.IsLinuxConsumption()
                // It should be a no-op for most tests
                app.UseMiddleware<AppServiceHeaderFixupMiddleware>();

                _startup.Configure(app, applicationLifetime, env, loggerFactory);
            }
        }

        private FunctionMetadataManager GetMetadataManager(IOptionsMonitor<ScriptApplicationHostOptions> optionsMonitor, IScriptHostManager manager, ILoggerFactory factory)
        {
            var workerOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };

            var managerServiceProvider = manager as IServiceProvider;

            var metadataProvider = new FunctionMetadataProvider(optionsMonitor, new OptionsWrapper<LanguageWorkerOptions>(workerOptions), NullLogger<FunctionMetadataProvider>.Instance, new TestMetricsLogger());
            var metadataManager = new FunctionMetadataManager(managerServiceProvider.GetService<IOptions<ScriptJobHostOptions>>(),
                metadataProvider, managerServiceProvider.GetService<IEnumerable<IFunctionProvider>>(),
                managerServiceProvider.GetService<IOptions<HttpWorkerOptions>>(), manager, factory);

            return metadataManager;
        }

        private class TestExtensionBundleManager : IExtensionBundleManager
        {
            public Task<ExtensionBundleDetails> GetExtensionBundleDetails() => Task.FromResult<ExtensionBundleDetails>(null);

            public Task<string> GetExtensionBundlePath(HttpClient httpClient = null) => Task.FromResult<string>(null);

            public Task<string> GetExtensionBundlePath() => Task.FromResult<string>(null);

            public bool IsExtensionBundleConfigured() => false;

            public bool IsLegacyExtensionBundle() => true;

        }

        private class PostConfigureServices
        {
            private readonly Action<IServiceCollection> _postConfigure;

            public PostConfigureServices(Action<IServiceCollection> postConfigure)
            {
                _postConfigure = postConfigure;
            }

            public void ConfigureServices(IServiceCollection services)
            {
                _postConfigure?.Invoke(services);
            }
        }
    }
}