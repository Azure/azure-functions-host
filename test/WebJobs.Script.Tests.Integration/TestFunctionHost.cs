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
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestFunctionHost : IDisposable
    {
        private readonly ScriptApplicationHostOptions _hostOptions;
        private readonly IHost _webHost;
        private readonly string _appRoot;
        private readonly string _webHostInstanceId = Guid.NewGuid().ToString()[..8];
        // we need to capture every provider created by host restarts
        private readonly List<TestLoggerProvider> _scriptHostLoggerProviders = new();
        private readonly WebJobsScriptHostService _hostService;

        private readonly Timer _stillRunningTimer;
        private readonly DateTimeOffset _created = DateTimeOffset.UtcNow;
        private readonly string _createdStack;
        private readonly string _id = Guid.NewGuid().ToString();

        private TestLoggerProvider _webHostLoggerProvider;
        private bool _timerFired = false;
        private bool _isDisposed = false;

        public TestFunctionHost(string scriptPath,
            Action<IServiceCollection> configureWebHostServices = null,
            Action<IWebJobsBuilder> configureScriptHostWebJobsBuilder = null,
            Action<IConfigurationBuilder> configureScriptHostAppConfiguration = null,
            Action<ILoggingBuilder> configureScriptHostLogging = null,
            Action<IServiceCollection> configureScriptHostServices = null,
            Action<IConfigurationBuilder> configureWebHostAppConfiguration = null)
            : this(scriptPath, Path.Combine(Path.GetTempPath(), "Functions"), Path.Combine(Path.GetTempPath(), @"FunctionsData"), configureWebHostServices, configureScriptHostWebJobsBuilder,
                configureScriptHostAppConfiguration, configureScriptHostLogging, configureScriptHostServices, configureWebHostAppConfiguration)
        {
        }

        public TestFunctionHost(string scriptPath, string logPath, string testDataPath = "",
            Action<IServiceCollection> configureWebHostServices = null,
            Action<IWebJobsBuilder> configureScriptHostWebJobsBuilder = null,
            Action<IConfigurationBuilder> configureScriptHostAppConfiguration = null,
            Action<ILoggingBuilder> configureScriptHostLogging = null,
            Action<IServiceCollection> configureScriptHostServices = null,
            Action<IConfigurationBuilder> configureWebHostAppConfiguration = null,
            bool addTestSettings = true)
        {
            _appRoot = scriptPath;

            // Ensure each host instance gets a unique log directory to prevent
            // FileSystemWatcher cross-contamination between hosts sharing the same path.
            logPath = Path.Combine(logPath, Guid.NewGuid().ToString("N")[..8]);

            _hostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = _appRoot,
                TestDataPath = testDataPath,
                LogPath = logPath,
                SecretsPath = Environment.CurrentDirectory, // not used
                HasParentScope = true
            };

            var builder = new HostBuilder()
                .ConfigureWebHost(webHostBuilder =>
                {
                    webHostBuilder.ConfigureLogging(b =>
                    {
                        _webHostLoggerProvider = new(_webHostInstanceId);
                        b.AddProvider(_webHostLoggerProvider);

                        b.AddFilter<TestLoggerProvider>(null, LogLevel.Trace)
                         .AddFilter<TestLoggerProvider>("Microsoft.AspNet", LogLevel.Warning)
                         .AddFilter<TestLoggerProvider>("Azure.Core", LogLevel.Warning);
                    });

                    webHostBuilder.UseTestServer();

                    // On the dev branch (WebHostBuilder), ConfigureServices lambdas run
                    // BEFORE UseStartup's Startup.ConfigureServices. Since Startup registers
                    // IFunctionMetadataManager via AddSingleton (not TryAdd), the Startup
                    // registration was appended after the test's Replace and became the "last"
                    // registration — the one DI resolves. This meant the REAL
                    // FunctionMetadataManager (with WorkerFunctionMetadataProvider) was used.
                    //
                    // With HostBuilder.ConfigureWebHost, host-level ConfigureServices runs
                    // AFTER Startup, so Replace would find and remove Startup's registration,
                    // leaving only the test's custom one (with null WorkerFunctionMetadataProvider).
                    // This broke tests that depend on WebHost channel initialization via
                    // WorkerFunctionMetadataProvider.InitializeChannelAsync.
                    //
                    // To preserve dev behavior, register the Replace BEFORE UseStartup so
                    // Startup's AddSingleton runs after and becomes the effective registration.
                    webHostBuilder.ConfigureServices(services =>
                    {
                        services.Replace(new ServiceDescriptor(typeof(IFunctionMetadataManager), sp =>
                        {
                            var montior = sp.GetService<IOptionsMonitor<ScriptApplicationHostOptions>>();
                            var scriptManager = sp.GetService<IScriptHostManager>();
                            var loggerFactory = sp.GetService<ILoggerFactory>();
                            var environment = sp.GetService<IEnvironment>();

                            return GetMetadataManager(montior, scriptManager, loggerFactory, environment);
                        }, ServiceLifetime.Singleton));

                        services.Replace(new ServiceDescriptor(typeof(ISecretManagerProvider), new TestSecretManagerProvider(new TestSecretManager())));
                    });

                    webHostBuilder.UseStartup<TestStartup>();

                    // In .NET 10, UseStartup<T> eagerly activates the startup class via
                    // ActivatorUtilities, so it cannot resolve services registered later.
                    // Apply configureWebHostServices after UseStartup instead of injecting
                    // it into TestStartup's constructor via PostConfigureServices.
                    if (configureWebHostServices is not null)
                    {
                        webHostBuilder.ConfigureServices(configureWebHostServices);
                    }
                });

            builder.ConfigureServices(services =>
            {
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

                services.AddSingleton<ISystemLoggerFactory, SystemLoggerFactory>();
                services.SkipDependencyValidation();
            });

            builder.ConfigureScriptHostWebJobsBuilder(scriptHostWebJobsBuilder =>
            {
                scriptHostWebJobsBuilder.AddAzureStorage();
                configureScriptHostWebJobsBuilder?.Invoke(scriptHostWebJobsBuilder);
            })
            .ConfigureScriptHostAppConfiguration(scriptHostConfigurationBuilder =>
            {
                if (addTestSettings)
                {
                    scriptHostConfigurationBuilder.AddTestSettings();
                }
                configureScriptHostAppConfiguration?.Invoke(scriptHostConfigurationBuilder);
            })
            .ConfigureScriptHostLogging(scriptHostLoggingBuilder =>
            {
                scriptHostLoggingBuilder.Services.AddSingleton<ILoggerProvider, TestLoggerProvider>(s =>
                {
                    var options = s.GetService<IOptions<ScriptJobHostOptions>>();
                    var shortInstanceId = options.Value.InstanceId[..8];
                    var loggerProvider = new TestLoggerProvider($"{_webHostInstanceId}->{shortInstanceId}");
                    _scriptHostLoggerProviders.Add(loggerProvider);
                    return loggerProvider;
                });
                scriptHostLoggingBuilder.AddFilter<TestLoggerProvider>(null, LogLevel.Trace);
                scriptHostLoggingBuilder.AddFilter<TestLoggerProvider>("Microsoft.AspNet", LogLevel.Warning);
                scriptHostLoggingBuilder.AddFilter<TestLoggerProvider>("Azure.Core", LogLevel.Warning);
                configureScriptHostLogging?.Invoke(scriptHostLoggingBuilder);
            })
            .ConfigureScriptHostServices(scriptHostServices =>
            {
                configureScriptHostServices?.Invoke(scriptHostServices);
            })
            .ConfigureAppConfiguration((builderContext, config) =>
            {
                // replace the default environment source with our own
                IConfigurationSource envVarsSource = config.Sources.OfType<EnvironmentVariablesConfigurationSource>().FirstOrDefault();
                if (envVarsSource != null)
                {
                    config.Sources.Remove(envVarsSource);
                }

                config.Add(new ScriptEnvironmentVariablesConfigurationSource());
                if (addTestSettings)
                {
                    config.AddTestSettings();
                }
                configureWebHostAppConfiguration?.Invoke(config);
            });

            _webHost = builder.Build();

            // The original code used new TestServer(builder) which internally starts the server.
            // With the HostBuilder pattern, we must explicitly start the host so that the
            // TestServer (registered via UseTestServer) initializes its application pipeline.
            _webHost.StartAsync().GetAwaiter().GetResult();

            HttpClient = _webHost.GetTestClient();
            HttpClient.Timeout = TimeSpan.FromMinutes(5);

            var environment = _webHost.Services.GetService<IEnvironment>();
            if (environment.IsAppService())
            {
                // host is configured to simulate an AppService environment
                // all normal requests will go through the Antares FrontEnd and receive this header
                HttpClient.DefaultRequestHeaders.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz");
            }

            var manager = _webHost.Services.GetService<IScriptHostManager>();
            _hostService = manager as WebJobsScriptHostService;

            // Wire up StopApplication calls as they behave in hosted scenarios
            var lifetime = WebHostServices.GetService<IApplicationLifetime>();
            lifetime.ApplicationStopping.Register(async () =>
            {
                try
                {
                    await _webHost.StopAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Host may already be disposed during test cleanup.
                }
            });

            StartAsync().GetAwaiter().GetResult();

            _stillRunningTimer = new Timer(StillRunningCallback, _webHost, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            // store off a bit of the creation stack for easier debugging if this host doesn't shut down.
            var stack = new StackTrace(true).ToString().Split(Environment.NewLine).Take(5);
            _createdStack = string.Join($"{Environment.NewLine}    ", stack);

            // cache startup logs since tests clear logs from time to time
            StartupLogs = GetScriptHostLogMessages();
        }

        public IList<LogMessage> StartupLogs { get; }

        public IServiceProvider JobHostServices => _hostService.Services;

        public IServiceProvider WebHostServices => _webHost.Services;

        public ScriptJobHostOptions ScriptOptions => JobHostServices.GetService<IOptions<ScriptJobHostOptions>>().Value;

        public ISecretManagerProvider SecretManagerProvider => _webHost.Services.GetService<ISecretManagerProvider>();

        public ISecretManager SecretManager => SecretManagerProvider.Current;

        public string LogPath => _hostOptions.LogPath;

        public string ScriptPath => _hostOptions.ScriptPath;

        public HttpClient HttpClient { get; private set; }

        public IHost WebHost => _webHost;

        /// <summary>
        /// Create a new HttpClient without default test configuration.
        /// </summary>
        public HttpClient CreateHttpClient()
        {
            var httpClient = _webHost.GetTestClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            return httpClient;
        }

        public async Task<string> GetMasterKeyAsync()
        {
            if (!SecretManagerProvider.SecretsEnabled)
            {
                return null;
            }

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
            await _hostService.RestartHostAsync("test", cancellationToken);
        }

        private void StillRunningCallback(object state)
        {
            var idProvider = JobHostServices?.GetService<IHostIdProvider>();
            var jobOptions = JobHostServices?.GetService<IOptions<ScriptJobHostOptions>>();

            if (idProvider == null || jobOptions == null)
            {
                return;
            }

            var functions = jobOptions?.Value.Functions ?? new[] { "" };

            string allowList = $"[{string.Join(", ", functions)}]";
            string hostId = idProvider.GetHostIdAsync(CancellationToken.None).Result;

            var ago = (int)(DateTime.UtcNow - _created).TotalSeconds;

            // This helps debugging tests that may not be disposing their hosts.
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"A host created at '{_created:yyyy-MM-dd HH:mm:ss}' ({ago}s ago) is still running. Details:");
            sb.AppendLine($"  Host id:      {hostId}");
            sb.AppendLine($"  Test host id: {_id}");
            sb.AppendLine($"  WebHost instance id: {_webHostInstanceId}");
            sb.AppendLine($"  ScriptRoot:   {jobOptions.Value.RootScriptPath}");
            sb.AppendLine($"  Allow list:   {allowList}");
            sb.AppendLine($"  The captured stack from this test host's constructor:");
            sb.AppendLine(_createdStack);

            Console.Write(sb.ToString());

            _timerFired = true;
        }

        private Task StartAsync()
        {
            bool exit = false;
            var startTask = Task.Run(async () =>
            {
                bool running = false;
                while (!running && !exit)
                {
                    running = await IsHostStarted();

                    if (!running)
                    {
                        await Task.Delay(50);
                    }
                }
            });

            if (startTask.Wait(TimeSpan.FromMinutes(1)))
            {
                return Task.CompletedTask;
            }
            else
            {
                exit = true;
                Console.WriteLine("---- FLUSHING LOG TO CONSOLE BEFORE FAILURE ----");
                Console.WriteLine(GetLog());
                throw new Exception("Functions Host timed out trying to start.");
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
        public IList<LogMessage> GetScriptHostLogMessages() => _scriptHostLoggerProviders.SelectMany(p => p.GetAllLogMessages()).OrderBy(p => p.Timestamp).ToList();
        public IEnumerable<LogMessage> GetScriptHostLogMessages(string category) => GetScriptHostLogMessages().Where(p => p.Category == category);

        /// <summary>
        /// The functions host has two logger providers -- one at the WebHost level and one at the ScriptHost level.
        /// These providers use different LoggerProviders, so it's important to know which one is receiving the logs.
        /// </summary>
        /// <returns>The messages from the WebHost LoggerProvider</returns>
        public IList<LogMessage> GetWebHostLogMessages() => _webHostLoggerProvider.GetAllLogMessages();
        public IEnumerable<LogMessage> GetWebHostLogMessages(string category) => GetWebHostLogMessages().Where(p => p.Category == category);

        public string GetLog() => string.Join(Environment.NewLine, GetScriptHostLogMessages().Concat(GetWebHostLogMessages()).OrderBy(m => m.Timestamp));

        public void ClearLogMessages()
        {
            _webHostLoggerProvider.ClearAllLogMessages();
            foreach (var provider in _scriptHostLoggerProviders)
            {
                provider.ClearAllLogMessages();
            }
        }

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
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "admin/host/status");

            if (SecretManagerProvider.SecretsEnabled)
            {
                // use admin key
                HostSecretsInfo secrets = await SecretManager.GetHostSecretsAsync();
                request.Headers.Add(AuthenticationLevelHandler.FunctionsKeyHeaderName, secrets.MasterKey);
            }
            else
            {
                // use admin jwt token
                string token = GenerateAdminJwtToken();
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsAsync<HostStatus>();
        }

        public string GenerateAdminJwtToken(string audience = null, string issuer = null, byte[] key = null)
        {
            audience = audience ?? string.Format(ScriptConstants.SiteAzureFunctionsUriFormat, Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName));
            issuer = issuer ?? string.Format(ScriptConstants.ScmSiteUriFormat, Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName));

            return JwtTokenHelper.CreateToken(DateTime.UtcNow.AddHours(1), audience, issuer, key);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                HttpClient.Dispose();
                
                _stillRunningTimer?.Change(-1, -1);
                _stillRunningTimer?.Dispose();

                if (_timerFired)
                {
                    Console.WriteLine($"The test host with id {_id} is now disposed.");
                }

                _isDisposed = true;
            }
        }

        public async Task<bool> IsHostStarted()
        {
            HostStatus status = await GetHostStatusAsync();
            return status.State == $"{ScriptHostState.Running}" || status.State == $"{ScriptHostState.Error}";
        }

        private class TestStartup
        {
            private WebHost.Startup _startup;

            public TestStartup(IConfiguration configuration)
            {
                _startup = new WebHost.Startup(configuration);
            }

            public void ConfigureServices(IServiceCollection services)
            {
                _startup.ConfigureServices(services);
            }

            public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
            {
                // This middleware is only added when env.IsLinuxConsumption()
                // It should be a no-op for most tests
                app.UseMiddleware<AppServiceHeaderFixupMiddleware>();

                _startup.Configure(app, env, loggerFactory);
            }
        }

        private FunctionMetadataManager GetMetadataManager(IOptionsMonitor<ScriptApplicationHostOptions> optionsMonitor, IScriptHostManager manager, ILoggerFactory factory, IEnvironment environment)
        {
            var workerOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };

            var mockWorkerRuntimeResolver = new Mock<IWorkerRuntimeResolver>();
            var mockOptions = new Mock<IOptionsMonitor<LanguageWorkerOptions>>();
            mockOptions.Setup(o => o.CurrentValue).Returns(workerOptions);
            mockOptions.Setup(o => o.OnChange(It.IsAny<Action<LanguageWorkerOptions, string>>())).Returns(Mock.Of<IDisposable>());

            var metadataOptions = new OptionsWrapper<FunctionMetadataOptions>(new FunctionMetadataOptions());

            var managerServiceProvider = manager as IServiceProvider;

            var metadataProvider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, new TestMetricsLogger(), mockWorkerRuntimeResolver.Object);
            var defaultProvider = new FunctionMetadataProvider(NullLogger<FunctionMetadataProvider>.Instance, null, metadataProvider, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()), SystemEnvironment.Instance);

            // In .NET 10, the IFunctionMetadataManager singleton may be resolved before the
            // script host is initialized, so ActiveHost is null and GetService returns null.
            // Provide a default ScriptJobHostOptions so _scriptOptions is never null at
            // construction time. InitializeServices() will replace it with the real options
            // once ActiveHostChanged fires.
            var scriptOptions = managerServiceProvider.GetService<IOptions<ScriptJobHostOptions>>()
                ?? Options.Create(new ScriptJobHostOptions());
            var metadataManager = new FunctionMetadataManager(scriptOptions, defaultProvider, manager, factory, environment, mockOptions.Object, metadataOptions);

            return metadataManager;
        }

        private class TestExtensionBundleManager : IExtensionBundleManager
        {
            public Task<string> GetExtensionBundleBinPathAsync() => Task.FromResult<string>(null);

            public Task<ExtensionBundleDetails> GetExtensionBundleDetails() => Task.FromResult<ExtensionBundleDetails>(null);

            public Task<string> GetExtensionBundlePath(HttpClient httpClient = null) => Task.FromResult<string>(null);

            public Task<string> GetExtensionBundlePath() => Task.FromResult<string>(null);

            public bool IsExtensionBundleConfigured() => false;

            public bool IsLegacyExtensionBundle() => true;

            public string GetOutdatedBundleVersion() { return string.Empty; /* no-op for test */ }
        }
    }
}