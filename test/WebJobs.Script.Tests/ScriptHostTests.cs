// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostTests : IClassFixture<ScriptHostTests.TestFixture>
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";
        private readonly TestFixture _fixture;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly TestEnvironment _testEnvironment = new TestEnvironment();

        private readonly ILoggerFactory _loggerFactory = new LoggerFactory();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public ScriptHostTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;

            _loggerFactory.AddProvider(_loggerProvider);
        }

        [Fact(Skip = "Add tests for HostJsonFileConfigurationSource, as this logic has moved there")]
        public void LoadHostConfig_DefaultsConfig_WhenFileMissing()
        {
            var path = Path.Combine(Path.GetTempPath(), @"does\not\exist\host.json");
            Assert.False(File.Exists(path));
            var logger = _loggerFactory.CreateLogger(LogCategories.Startup);
            //var config = ScriptHost.LoadHostConfig(path, logger);
            //Assert.Equal(0, config.Properties().Count());

            //var logMessage = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).Single();
            //Assert.Equal("No host configuration file found. Using default.", logMessage);
        }

        [Fact(Skip = "Add tests for HostJsonFileConfigurationSource, as this logic has moved there")]
        public void LoadHostConfig_LoadsConfigFile()
        {
            var path = Path.Combine(TestHelpers.FunctionsTestDirectory, "host.json");
            File.WriteAllText(path, "{ id: '123xyz' }");
            var logger = _loggerFactory.CreateLogger(LogCategories.Startup);
            //var config = ScriptHost.LoadHostConfig(path, logger);
            //Assert.Equal(1, config.Properties().Count());
            //Assert.Equal("123xyz", (string)config["id"]);
        }

        [Fact(Skip = "Add tests for HostJsonFileConfigurationSource, as this logic has moved there")]
        public void LoadHostConfig_ParseError_Throws()
        {
            var path = Path.Combine(TestHelpers.FunctionsTestDirectory, "host.json");
            File.WriteAllText(path, "{ blah");
            //JObject config = null;
            var logger = _loggerFactory.CreateLogger(LogCategories.Startup);
            var ex = Assert.Throws<FormatException>(() =>
            {
                //config = ScriptHost.LoadHostConfig(path, logger);
            });
            Assert.Equal($"Unable to parse host configuration file '{path}'.", ex.Message);
        }

        [Fact]
        public static async Task OnDebugModeFileChanged_TriggeredWhenDebugFileUpdated()
        {
            var host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost(runStartupHostedServices: true)
                .Build();

            ScriptHost scriptHost = host.GetScriptHost();
            string debugSentinelFilePath = Path.Combine(scriptHost.ScriptOptions.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);

            // Write the initial file.
            if (!File.Exists(debugSentinelFilePath))
            {
                File.WriteAllText(debugSentinelFilePath, string.Empty);
            }

            // first put the host into a non-debug state
            var debugState = host.Services.GetService<IDebugStateProvider>();
            debugState.LastDebugNotify = DateTime.MinValue;

            await host.StartAsync();

            await TestHelpers.Await(() =>
            {
                return !scriptHost.InDebugMode;
            },
            userMessageCallback: () => $"Expected InDebugMode to be false. Now: {DateTime.UtcNow}; Sentinel LastWriteTime: {File.GetLastWriteTimeUtc(debugSentinelFilePath)}; LastDebugNotify: {debugState.LastDebugNotify}.");

            // verify that our file watcher for the debug sentinel file is configured
            // properly by touching the file and ensuring that our host goes into
            // debug mode
            File.SetLastWriteTimeUtc(debugSentinelFilePath, DateTime.UtcNow);

            await TestHelpers.Await(() =>
            {
                return scriptHost.InDebugMode;
            }, userMessageCallback: () => "InDebugMode never set to true.");
        }

        [Fact]
        public void InDebugMode_ReturnsExpectedValue()
        {
            ScriptHost host = _fixture.ScriptHost;
            var debugState = _fixture.Host.Services.GetService<IDebugStateProvider>();

            debugState.LastDebugNotify = DateTime.MinValue;
            Assert.False(host.InDebugMode);

            debugState.LastDebugNotify = DateTime.UtcNow - TimeSpan.FromSeconds(60 * ScriptHost.DebugModeTimeoutMinutes);
            Assert.False(host.InDebugMode);

            debugState.LastDebugNotify = DateTime.UtcNow - TimeSpan.FromSeconds(60 * (ScriptHost.DebugModeTimeoutMinutes - 1));
            Assert.True(host.InDebugMode);
        }

        [Fact]
        public void FileLoggingEnabled_ReturnsExpectedValue()
        {
            ScriptHost host = _fixture.ScriptHost;
            var debugState = _fixture.Host.Services.GetService<IDebugStateProvider>();
            var debugManager = _fixture.Host.Services.GetService<IDebugManager>();
            var fileLoggingState = _fixture.Host.Services.GetService<IFileLoggingStatusManager>();

            host.ScriptOptions.FileLoggingMode = FileLoggingMode.DebugOnly;
            debugState.LastDebugNotify = DateTime.MinValue;
            Assert.False(fileLoggingState.IsFileLoggingEnabled);
            debugManager.NotifyDebug();
            Assert.True(fileLoggingState.IsFileLoggingEnabled);

            host.ScriptOptions.FileLoggingMode = FileLoggingMode.Never;
            Assert.False(fileLoggingState.IsFileLoggingEnabled);

            host.ScriptOptions.FileLoggingMode = FileLoggingMode.Always;
            Assert.True(fileLoggingState.IsFileLoggingEnabled);
            debugState.LastDebugNotify = DateTime.MinValue;
            Assert.True(fileLoggingState.IsFileLoggingEnabled);
        }

        [Fact]
        public void NotifyDebug_UpdatesDebugMarkerFileAndTimestamp()
        {
            ScriptHost host = _fixture.ScriptHost;

            var debugState = _fixture.Host.Services.GetService<IDebugStateProvider>();
            var debugManager = _fixture.Host.Services.GetService<IDebugManager>();

            string debugSentinelFileName = Path.Combine(host.ScriptOptions.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);
            File.Delete(debugSentinelFileName);
            debugState.LastDebugNotify = DateTime.MinValue;

            Assert.False(host.InDebugMode);

            DateTime lastDebugNotify = debugState.LastDebugNotify;
            debugManager.NotifyDebug();
            Assert.True(host.InDebugMode);
            Assert.True(File.Exists(debugSentinelFileName));
            string text = File.ReadAllText(debugSentinelFileName);
            Assert.Equal("This is a system managed marker file used to control runtime debug mode behavior.", text);
            Assert.True(debugState.LastDebugNotify > lastDebugNotify);

            Thread.Sleep(500);

            DateTime lastModified = File.GetLastWriteTime(debugSentinelFileName);
            lastDebugNotify = debugState.LastDebugNotify;
            debugManager.NotifyDebug();
            Assert.True(host.InDebugMode);
            Assert.True(File.Exists(debugSentinelFileName));
            Assert.True(File.GetLastWriteTime(debugSentinelFileName) > lastModified);
            Assert.True(debugState.LastDebugNotify > lastDebugNotify);
        }

        [Fact]
        public void NotifyDebug_HandlesExceptions()
        {
            ScriptHost host = _fixture.ScriptHost;
            string debugSentinelFileName = Path.Combine(host.ScriptOptions.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);

            var debugManager = _fixture.Host.Services.GetService<IDebugManager>();

            try
            {
                debugManager.NotifyDebug();
                Assert.True(host.InDebugMode);

                var attributes = File.GetAttributes(debugSentinelFileName);
                attributes |= FileAttributes.ReadOnly;
                File.SetAttributes(debugSentinelFileName, attributes);
                Assert.True(host.InDebugMode);

                debugManager.NotifyDebug();
            }
            finally
            {
                File.SetAttributes(debugSentinelFileName, FileAttributes.Normal);
                File.Delete(debugSentinelFileName);
            }
        }

        [Fact]
        public void Version_ReturnsAssemblyVersion()
        {
            Assembly assembly = typeof(ScriptHost).Assembly;
            AssemblyFileVersionAttribute fileVersionAttribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();

            string version = ScriptHost.Version;
            Assert.Equal(fileVersionAttribute.Version, version);
        }

        [Fact]
        public void GetAssemblyFileVersion_Unknown()
        {
            var asm = new AssemblyMock();
            var version = ScriptHost.GetAssemblyFileVersion(asm);

            Assert.Equal("Unknown", version);
        }

        [Fact]
        public void GetAssemblyFileVersion_ReturnsVersion()
        {
            var fileAttr = new AssemblyFileVersionAttribute("1.2.3.4");
            var asmMock = new Mock<AssemblyMock>();
            asmMock.Setup(a => a.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true))
               .Returns(new Attribute[] { fileAttr })
               .Verifiable();

            var version = ScriptHost.GetAssemblyFileVersion(asmMock.Object);

            Assert.Equal("1.2.3.4", version);
            asmMock.Verify();
        }

        [Fact(Skip = "Host.json parsing logic moved to HostJsonFileConfigurationSource. Move test")]
        public void Create_InvalidHostJson_ThrowsInformativeException()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Invalid");

            var scriptConfig = new ScriptJobHostOptions()
            {
                RootScriptPath = rootPath
            };

            //var environment = new Mock<IScriptHostEnvironment>();
            //var eventManager = new Mock<IScriptEventManager>();
            //var host = new ScriptHost(environment.Object, eventManager.Object, scriptConfig, _settingsManager);

            //var ex = Assert.Throws<FormatException>(() =>
            //{
            //    host.Initialize();
            //});

            //var configFilePath = Path.Combine(rootPath, "host.json");
            //Assert.Equal($"Unable to parse host configuration file '{configFilePath}'.", ex.Message);
            //Assert.Equal("Invalid property identifier character: ~. Path '', line 2, position 4.", ex.InnerException.Message);
        }

        [Theory]
        [InlineData("host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
        [InlineData("function.test")]
        [InlineData("function0.1")]
        public async Task Initialize_InvalidFunctionNames_DoesNotCreateFunctionAndLogsFailure(string functionName)
        {
            string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string invalidFunctionNamePath = Path.Combine(rootPath, functionName);
            try
            {
                Directory.CreateDirectory(invalidFunctionNamePath);

                JObject config = new JObject();
                config["version"] = "2.0";
                config["id"] = ID;

                File.WriteAllText(Path.Combine(rootPath, ScriptConstants.HostMetadataFileName), config.ToString());
                File.WriteAllText(Path.Combine(invalidFunctionNamePath, ScriptConstants.FunctionMetadataFileName), string.Empty);

                var environment = new Mock<IScriptJobHostEnvironment>();
                var eventManager = new Mock<IScriptEventManager>();

                IHost host = new HostBuilder()
                    .ConfigureDefaultTestWebScriptHost(o =>
                    {
                        o.ScriptPath = rootPath;
                    })
                    .Build();

                var scriptHost = host.GetScriptHost();
                await scriptHost.InitializeAsync();

                Assert.Equal(1, scriptHost.FunctionErrors.Count);
                Assert.Equal(functionName, scriptHost.FunctionErrors.First().Key);
                Assert.Equal($"'{functionName}' is not a valid function name.", scriptHost.FunctionErrors.First().Value.First());
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Initialize_WithInvalidSiteExtensionVersion_Throws(string extensionVersion)
        {
            try
            {
                string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                var environment = new TestEnvironment();
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, extensionVersion);

                EnvironmentExtensions.BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SiteExtensions", "Functions", "2.0.0");

                IHost host = new HostBuilder()
                    .ConfigureServices(s =>
                    {
                        s.AddSingleton<IEnvironment>(environment);
                    })
                    .ConfigureDefaultTestWebScriptHost(o =>
                    {
                        o.ScriptPath = rootPath;
                    })
                    .Build();

                var scriptHost = host.GetScriptHost();
                await Assert.ThrowsAsync<HostInitializationException>(() => scriptHost.InitializeAsync());
            }
            finally
            {
                EnvironmentExtensions.BaseDirectory = null;
            }
        }

        [Fact]
        public async Task Initialize_WithLatestSiteExtensionVersion_LogsWarning()
        {
            try
            {
                using (var tempDirectory = new TempDirectory())
                {
                    string rootPath = Path.Combine(tempDirectory.Path, Guid.NewGuid().ToString());
                    Directory.CreateDirectory(rootPath);
                    var loggerProvider = new TestLoggerProvider();
                    var environment = new TestEnvironment();
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));
                    environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsExtensionVersion, "latest");

                    EnvironmentExtensions.BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SiteExtensions", "Functions", "2.0.0");

                    IHost host = new HostBuilder()
                        .ConfigureServices(s =>
                        {
                            s.AddSingleton<IEnvironment>(environment);
                        })
                        .ConfigureLogging(l =>
                        {
                            l.AddProvider(loggerProvider);
                        })
                        .ConfigureDefaultTestWebScriptHost(o =>
                        {
                            o.ScriptPath = rootPath;
                        })
                        .Build();

                    var scriptHost = host.GetScriptHost();
                    await scriptHost.InitializeAsync();
                    Assert.Single(loggerProvider.GetAllLogMessages(), m => m.Level == LogLevel.Warning && m.FormattedMessage.StartsWith("Site extension version currently set to 'latest'."));
                }
            }
            finally
            {
                EnvironmentExtensions.BaseDirectory = null;
            }
        }

        // TODO: Newer TODO - ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)
        // TODO: Move this test into a new WebJobsCoreScriptBindingProvider class since
        // the functionality moved. Also add tests for the ServiceBus config, etc.
        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_Queues()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject queuesConfig = new JObject();
            config["queues"] = queuesConfig;

            //var scriptConfig = new ScriptHostOptions();
            //scriptConfig.HostConfig.HostConfigMetadata = config;

            //new JobHost(scriptConfig.HostConfig).CreateMetadataProvider(); // will cause extensions to initialize and consume config metadata.

            //Assert.Equal(60 * 1000, scriptConfig.HostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            //Assert.Equal(16, scriptConfig.HostConfig.Queues.BatchSize);
            //Assert.Equal(5, scriptConfig.HostConfig.Queues.MaxDequeueCount);
            //Assert.Equal(8, scriptConfig.HostConfig.Queues.NewBatchThreshold);
            //Assert.Equal(TimeSpan.Zero, scriptConfig.HostConfig.Queues.VisibilityTimeout);

            //queuesConfig["maxPollingInterval"] = 5000;
            //queuesConfig["batchSize"] = 17;
            //queuesConfig["maxDequeueCount"] = 3;
            //queuesConfig["newBatchThreshold"] = 123;
            //queuesConfig["visibilityTimeout"] = "00:00:30";

            //scriptConfig = new ScriptHostConfiguration();
            //scriptConfig.HostConfig.HostConfigMetadata = config;
            //new JobHost(scriptConfig.HostConfig).CreateMetadataProvider(); // will cause extensions to initialize and consume config metadata.

            //Assert.Equal(5000, scriptConfig.HostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            //Assert.Equal(17, scriptConfig.HostConfig.Queues.BatchSize);
            //Assert.Equal(3, scriptConfig.HostConfig.Queues.MaxDequeueCount);
            //Assert.Equal(123, scriptConfig.HostConfig.Queues.NewBatchThreshold);
            //Assert.Equal(TimeSpan.FromSeconds(30), scriptConfig.HostConfig.Queues.VisibilityTimeout);
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_Http()
        {
            //JObject config = new JObject();
            //config["id"] = ID;
            //JObject http = new JObject();
            //config["http"] = http;

            //JobHostConfiguration hostConfig = new JobHostConfiguration();
            //WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            //provider.Initialize();

            //IExtensionRegistry extensions = hostConfig.GetService<IExtensionRegistry>();
            //var httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();

            //Assert.Equal(HttpExtensionConstants.DefaultRoutePrefix, httpConfig.RoutePrefix);
            //Assert.Equal(false, httpConfig.DynamicThrottlesEnabled);
            //Assert.Equal(DataflowBlockOptions.Unbounded, httpConfig.MaxConcurrentRequests);
            //Assert.Equal(DataflowBlockOptions.Unbounded, httpConfig.MaxOutstandingRequests);

            //http["routePrefix"] = "myprefix";
            //http["dynamicThrottlesEnabled"] = true;
            //http["maxConcurrentRequests"] = 5;
            //http["maxOutstandingRequests"] = 10;

            //hostConfig = new JobHostConfiguration();
            //provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            //provider.Initialize();

            //extensions = hostConfig.GetService<IExtensionRegistry>();
            //httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();

            //Assert.Equal("myprefix", httpConfig.RoutePrefix);
            //Assert.True(httpConfig.DynamicThrottlesEnabled);
            //Assert.Equal(5, httpConfig.MaxConcurrentRequests);
            //Assert.Equal(10, httpConfig.MaxOutstandingRequests);
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_Blobs()
        {
            //JObject config = new JObject();
            //config["id"] = ID;
            //JObject blobsConfig = new JObject();
            //config["blobs"] = blobsConfig;

            //JobHostConfiguration hostConfig = new JobHostConfiguration();

            //WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            //provider.Initialize();

            //Assert.False(hostConfig.Blobs.CentralizedPoisonQueue);

            //blobsConfig["centralizedPoisonQueue"] = true;

            //provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            //provider.Initialize();

            //Assert.True(hostConfig.Blobs.CentralizedPoisonQueue);
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_Singleton()
        {
            //JObject config = new JObject();
            //config["id"] = ID;
            //JObject singleton = new JObject();
            //config["singleton"] = singleton;
            //ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            //ScriptHost.ApplyConfiguration(config, scriptConfig);

            //Assert.Equal(ID, scriptConfig.HostConfig.HostId);
            //Assert.Equal(15, scriptConfig.HostConfig.Singleton.LockPeriod.TotalSeconds);
            //Assert.Equal(1, scriptConfig.HostConfig.Singleton.ListenerLockPeriod.TotalMinutes);
            //Assert.Equal(1, scriptConfig.HostConfig.Singleton.ListenerLockRecoveryPollingInterval.TotalMinutes);
            //Assert.Equal(TimeSpan.MaxValue, scriptConfig.HostConfig.Singleton.LockAcquisitionTimeout);
            //Assert.Equal(5, scriptConfig.HostConfig.Singleton.LockAcquisitionPollingInterval.TotalSeconds);

            //singleton["lockPeriod"] = "00:00:17";
            //singleton["listenerLockPeriod"] = "00:00:22";
            //singleton["listenerLockRecoveryPollingInterval"] = "00:00:33";
            //singleton["lockAcquisitionTimeout"] = "00:05:00";
            //singleton["lockAcquisitionPollingInterval"] = "00:00:08";

            //ScriptHost.ApplyConfiguration(config, scriptConfig);

            //Assert.Equal(17, scriptConfig.HostConfig.Singleton.LockPeriod.TotalSeconds);
            //Assert.Equal(22, scriptConfig.HostConfig.Singleton.ListenerLockPeriod.TotalSeconds);
            //Assert.Equal(33, scriptConfig.HostConfig.Singleton.ListenerLockRecoveryPollingInterval.TotalSeconds);
            //Assert.Equal(5, scriptConfig.HostConfig.Singleton.LockAcquisitionTimeout.TotalMinutes);
            //Assert.Equal(8, scriptConfig.HostConfig.Singleton.LockAcquisitionPollingInterval.TotalSeconds);
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource) - Also validate in setup test")]
        public void ApplyConfiguration_FileWatching()
        {
            //JObject config = new JObject();
            //config["id"] = ID;

            //ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            //Assert.True(scriptConfig.FileWatchingEnabled);

            //scriptConfig = new ScriptHostConfiguration();
            //config["fileWatchingEnabled"] = new JValue(true);
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.True(scriptConfig.FileWatchingEnabled);
            //Assert.Equal(1, scriptConfig.WatchDirectories.Count);
            //Assert.Equal("node_modules", scriptConfig.WatchDirectories.ElementAt(0));

            //scriptConfig = new ScriptHostConfiguration();
            //config["fileWatchingEnabled"] = new JValue(false);
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.False(scriptConfig.FileWatchingEnabled);

            //scriptConfig = new ScriptHostConfiguration();
            //config["fileWatchingEnabled"] = new JValue(true);
            //config["watchDirectories"] = new JArray("Shared", "Tools");
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.True(scriptConfig.FileWatchingEnabled);
            //Assert.Equal(3, scriptConfig.WatchDirectories.Count);
            //Assert.Equal("node_modules", scriptConfig.WatchDirectories.ElementAt(0));
            //Assert.Equal("Shared", scriptConfig.WatchDirectories.ElementAt(1));
            //Assert.Equal("Tools", scriptConfig.WatchDirectories.ElementAt(2));
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_AllowPartialHostStartup()
        {
            //JObject config = new JObject();
            //config["id"] = ID;

            //ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            //Assert.False(scriptConfig.HostConfig.AllowPartialHostStartup);

            //// we default it to true
            //scriptConfig = new ScriptHostConfiguration();
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.True(scriptConfig.HostConfig.AllowPartialHostStartup);

            //// explicit setting can override our default
            //scriptConfig = new ScriptHostConfiguration();
            //config["allowPartialHostStartup"] = new JValue(true);
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.True(scriptConfig.HostConfig.AllowPartialHostStartup);

            //// explicit setting can override our default
            //scriptConfig = new ScriptHostConfiguration();
            //config["allowPartialHostStartup"] = new JValue(false);
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.False(scriptConfig.HostConfig.AllowPartialHostStartup);
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_AppliesFunctionsFilter()
        {
            //JObject config = new JObject();
            //config["id"] = ID;

            //ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            //Assert.Null(scriptConfig.Functions);

            //config["functions"] = new JArray("Function1", "Function2");

            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.Equal(2, scriptConfig.Functions.Count);
            //Assert.Equal("Function1", scriptConfig.Functions.ElementAt(0));
            //Assert.Equal("Function2", scriptConfig.Functions.ElementAt(1));
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyConfiguration_ClearsFunctionsFilter()
        {
            // A previous bug wouldn't properly clear the filter if you removed it.
            //JObject config = new JObject();
            //config["id"] = ID;

            //ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            //Assert.Null(scriptConfig.Functions);

            //config["functions"] = new JArray("Function1", "Function2");

            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.Equal(2, scriptConfig.Functions.Count);
            //Assert.Equal("Function1", scriptConfig.Functions.ElementAt(0));
            //Assert.Equal("Function2", scriptConfig.Functions.ElementAt(1));

            //config.Remove("functions");

            //ScriptHost.ApplyConfiguration(config, scriptConfig);

            //Assert.Null(scriptConfig.Functions);
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void ApplyHostHealthMonitorConfig_AppliesExpectedSettings()
        {
            //JObject config = JObject.Parse("{ }");

            //ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            //ScriptHost.ApplyConfiguration(config, scriptConfig);

            //Assert.True(scriptConfig.HostHealthMonitor.Enabled);
            //Assert.Equal(TimeSpan.FromSeconds(10), scriptConfig.HostHealthMonitor.HealthCheckInterval);
            //Assert.Equal(TimeSpan.FromMinutes(2), scriptConfig.HostHealthMonitor.HealthCheckWindow);
            //Assert.Equal(6, scriptConfig.HostHealthMonitor.HealthCheckThreshold);
            //Assert.Equal(HostHealthMonitorConfiguration.DefaultCounterThreshold, scriptConfig.HostHealthMonitor.CounterThreshold);

            //// now set custom configuration and verify
            //config = JObject.Parse(@"
            // {
            //     'healthMonitor': {
            //         'enabled': false
            //     }
            // }");
            //scriptConfig = new ScriptHostConfiguration();
            //ScriptHost.ApplyConfiguration(config, scriptConfig);

            //Assert.False(scriptConfig.HostHealthMonitor.Enabled);

            //config = JObject.Parse(@"
            // {
            //     'healthMonitor': {
            //         'enabled': true,
            //         'healthCheckInterval': '00:00:07',
            //         'healthCheckWindow': '00:07:00',
            //         'healthCheckThreshold': 77,
            //         'counterThreshold': 0.77
            //     }

            // }");
            //scriptConfig = new ScriptHostConfiguration();
            //ScriptHost.ApplyConfiguration(config, scriptConfig);

            //Assert.True(scriptConfig.HostHealthMonitor.Enabled);
            //Assert.Equal(TimeSpan.FromSeconds(7), scriptConfig.HostHealthMonitor.HealthCheckInterval);
            //Assert.Equal(TimeSpan.FromMinutes(7), scriptConfig.HostHealthMonitor.HealthCheckWindow);
            //Assert.Equal(77, scriptConfig.HostHealthMonitor.HealthCheckThreshold);
            //Assert.Equal(0.77F, scriptConfig.HostHealthMonitor.CounterThreshold);
        }

        [Fact]
        public void TryGetFunctionFromException_FunctionMatch()
        {
            string stack = "TypeError: Cannot read property 'is' of undefined\n" +
                           "at Timeout._onTimeout(D:\\home\\site\\wwwroot\\HttpTriggerNode\\index.js:7:35)\n" +
                           "at tryOnTimeout (timers.js:224:11)\n" +
                           "at Timer.listOnTimeout(timers.js:198:5)";
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            var exception = new InvalidOperationException(stack);

            // no match - empty functions
            bool result = ScriptHost.TryGetFunctionFromException(functions, exception, out FunctionDescriptor functionResult);
            Assert.False(result);
            Assert.Null(functionResult);

            // no match - one non-matching function
            FunctionMetadata metadata = new FunctionMetadata
            {
                Name = "SomeFunction",
                ScriptFile = "D:\\home\\site\\wwwroot\\SomeFunction\\index.js"
            };
            FunctionDescriptor function = new FunctionDescriptor("TimerFunction", new TestInvoker(), metadata, new Collection<ParameterDescriptor>(), null, null, null);
            functions.Add(function);
            result = ScriptHost.TryGetFunctionFromException(functions, exception, out functionResult);
            Assert.False(result);
            Assert.Null(functionResult);

            // match - exact
            metadata = new FunctionMetadata
            {
                Name = "HttpTriggerNode",
                ScriptFile = "D:\\home\\site\\wwwroot\\HttpTriggerNode\\index.js"
            };
            function = new FunctionDescriptor("TimerFunction", new TestInvoker(), metadata, new Collection<ParameterDescriptor>(), null, null, null);
            functions.Add(function);
            result = ScriptHost.TryGetFunctionFromException(functions, exception, out functionResult);
            Assert.True(result);
            Assert.Same(function, functionResult);

            // match - different file from the same function
            stack = "TypeError: Cannot read property 'is' of undefined\n" +
                           "at Timeout._onTimeout(D:\\home\\site\\wwwroot\\HttpTriggerNode\\npm\\lib\\foo.js:7:35)\n" +
                           "at tryOnTimeout (timers.js:224:11)\n" +
                           "at Timer.listOnTimeout(timers.js:198:5)";
            exception = new InvalidOperationException(stack);
            result = ScriptHost.TryGetFunctionFromException(functions, exception, out functionResult);
            Assert.True(result);
            Assert.Same(function, functionResult);
        }

        [Theory]
        [InlineData("myproxy")]
        [InlineData("my proxy")]
        [InlineData("my proxy %")]
        public void UpdateProxyName(string proxyName)
        {
            Assert.Equal("myproxy", ProxyMetadataManager.NormalizeProxyName(proxyName));
        }

        [Fact]
        public void IsSingleLanguage_Returns_True()
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "funcJs1",
                Language = "node"
            };
            FunctionMetadata func2 = new FunctionMetadata()
            {
                Name = "funcJs2",
                Language = "node"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                func1, func2
            };
            Assert.True(Utility.IsSingleLanguage(functionsList, null));
        }

        [Fact]
        public void IsSingleLanguage_Returns_True_Proxy()
        {
            FunctionMetadata proxy = new FunctionMetadata()
            {
                Name = "proxy",
                IsProxy = true
            };
            FunctionMetadata funcJs = new FunctionMetadata()
            {
                Name = "funcJs",
                Language = "node"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                proxy, funcJs
            };
            Assert.True(Utility.IsSingleLanguage(functionsList, null));
        }

        [Fact]
        public void IsSingleLanguage_Returns_True_OnlyProxies()
        {
            FunctionMetadata proxy1 = new FunctionMetadata()
            {
                Name = "proxy",
                IsProxy = true
            };
            FunctionMetadata proxy2 = new FunctionMetadata()
            {
                Name = "proxy",
                IsProxy = true
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                proxy1, proxy2
            };
            Assert.True(Utility.IsSingleLanguage(functionsList, null));
        }

        [Fact]
        public void IsSingleLanguage_FunctionsWorkerRuntime_Set_Returns_True_OnlyProxies()
        {
            FunctionMetadata proxy1 = new FunctionMetadata()
            {
                Name = "proxy",
                IsProxy = true
            };
            FunctionMetadata proxy2 = new FunctionMetadata()
            {
                Name = "proxy",
                IsProxy = true
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                proxy1, proxy2
            };
            Assert.True(Utility.IsSingleLanguage(functionsList, "python"));
        }

        [Fact]
        public void IsSingleLanguage_FunctionsWorkerRuntime_Set_Returns_True()
        {
            FunctionMetadata funcPython1 = new FunctionMetadata()
            {
                Name = "funcPython1",
                Language = "python",
            };
            FunctionMetadata funcJs = new FunctionMetadata()
            {
                Name = "funcJs",
                Language = "node"
            };
            FunctionMetadata funcCSharp1 = new FunctionMetadata()
            {
                Name = "funcCSharp1",
                Language = "CSharp",
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcPython1, funcJs, funcCSharp1
            };
            Assert.True(Utility.IsSingleLanguage(functionsList, "node"));
        }

        [Fact]
        public void IsSingleLanguage_FunctionsWorkerRuntime_Set_Returns_False()
        {
            FunctionMetadata funcPython1 = new FunctionMetadata()
            {
                Name = "funcPython1",
                Language = "python",
            };
            FunctionMetadata funcCSharp1 = new FunctionMetadata()
            {
                Name = "funcCSharp1",
                Language = "CSharp",
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcPython1, funcCSharp1
            };
            Assert.False(Utility.IsSingleLanguage(functionsList, "node"));
        }

        [Fact]
        public void IsSingleLanguage_Returns_False()
        {
            FunctionMetadata funcJs1 = new FunctionMetadata()
            {
                Name = "funcJs1",
                Language = "node"
            };
            FunctionMetadata funcPython1 = new FunctionMetadata()
            {
                Name = "funcPython1",
                Language = "python",
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcJs1, funcPython1
            };
            Assert.False(Utility.IsSingleLanguage(functionsList, null));
        }

        [Fact]
        public void IsSingleLanguage_FunctionsList_Null_FunctionsWorkerRuntime_Throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Utility.IsSingleLanguage(null, "dotnet"));
        }

        [Fact]
        public void IsSingleLanguage_FunctionsList_Null_Throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Utility.IsSingleLanguage(null, null));
        }

        [Fact]
        public void IsSupportedRuntime_Returns_False()
        {
            Assert.False(Utility.IsSupportedRuntime(LanguageWorkerConstants.DotNetLanguageWorkerName, TestHelpers.GetTestWorkerConfigs()));
        }

        [Fact]
        public void IsSupportedRuntime_Returns_True()
        {
            Assert.True(Utility.IsSupportedRuntime(LanguageWorkerConstants.NodeLanguageWorkerName, TestHelpers.GetTestWorkerConfigs()));
        }

        [Theory]
        [InlineData("CSharp")]
        [InlineData("DotNetAssembly")]
        public void IsDotNetLanguageFunction_Returns_True(string functionLanguage)
        {
            Assert.True(Utility.IsDotNetLanguageFunction(functionLanguage));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("someLang")]
        public void IsDotNetLanguageFunction_Returns_False(string functionLanguage)
        {
            Assert.False(Utility.IsDotNetLanguageFunction(functionLanguage));
        }

        private static IEnumerable<FunctionMetadata> GetDotNetFunctionsMetadata()
        {
            FunctionMetadata funcCS1 = new FunctionMetadata()
            {
                Name = "funcCS1",
                Language = "csharp"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcCS1
            };
            return functionsList;
        }

        [Fact]
        public void VerifyFunctionsMatchSpecifiedLanguage_Throws_For_UnmatchedLanguage_With_RuntimeLanguage_Specified()
        {
            FunctionMetadata funcJS1 = new FunctionMetadata()
            {
                Name = "funcJS1",
                Language = "node"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcJS1
            };

            HostInitializationException ex = Assert.Throws<HostInitializationException>(() => Utility.VerifyFunctionsMatchSpecifiedLanguage(functionsList, LanguageWorkerConstants.DotNetLanguageWorkerName, false, false));
            Assert.Equal($"Did not find functions with language [{LanguageWorkerConstants.DotNetLanguageWorkerName}].", ex.Message);
        }

        [Fact]
        public void VerifyFunctionsMatchSpecifiedLanguage_NoThrow_For_MixedLanguageMatching_With_RuntimeLanguage_Specified()
        {
            FunctionMetadata funcJS1 = new FunctionMetadata()
            {
                Name = "funcJS1",
                Language = "node"
            };
            // CSharp matches dotnet so we should be able to initialize the host
            FunctionMetadata funcCS1 = new FunctionMetadata()
            {
                Name = "funcJS1",
                Language = "csharp"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcJS1, funcCS1
            };

            Utility.VerifyFunctionsMatchSpecifiedLanguage(functionsList, LanguageWorkerConstants.DotNetLanguageWorkerName, false, false);
        }

        [Fact]
        public void VerifyFunctionsMatchSpecifiedLanguage_NoThrow_For_SingleLanguage_Without_RuntimeLanguage_Specified()
        {
            FunctionMetadata funcJS1 = new FunctionMetadata()
            {
                Name = "funcJS1",
                Language = "node"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcJS1
            };

            Utility.VerifyFunctionsMatchSpecifiedLanguage(functionsList, string.Empty, false, false);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        public void VerifyFunctionsMatchSpecifiedLanguage_NoThrow_For_HttpWorkerOrPlaceholderMode(bool placeholderMode, bool httpWorker)
        {
            FunctionMetadata funcJS1 = new FunctionMetadata()
            {
                Name = "funcJS1"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcJS1
            };

            Utility.VerifyFunctionsMatchSpecifiedLanguage(functionsList, string.Empty, placeholderMode, httpWorker);
        }

        [Fact]
        public void VerifyFunctionsMatchSpecifiedLanguage_Throws_For_NotSingleLanguage_Without_RuntimeLanguage_Specified()
        {
            FunctionMetadata funcJS1 = new FunctionMetadata()
            {
                Name = "funcJS1",
                Language = "node"
            };
            FunctionMetadata funcCS1 = new FunctionMetadata()
            {
                Name = "funcCS1",
                Language = "csharp"
            };
            IEnumerable<FunctionMetadata> functionsList = new Collection<FunctionMetadata>()
            {
                funcJS1, funcCS1
            };

            HostInitializationException ex = Assert.Throws<HostInitializationException>(() => Utility.VerifyFunctionsMatchSpecifiedLanguage(functionsList, string.Empty, false, false));
            Assert.Equal($"Found functions with more than one language. Select a language for your function app by specifying {LanguageWorkerConstants.FunctionWorkerRuntimeSettingName} AppSetting", ex.Message);
        }

        [Fact]
        public void ValidateFunction_ValidatesHttpRoutes()
        {
            var httpFunctions = new Dictionary<string, HttpTriggerAttribute>();

            // first add an http function
            var metadata = new FunctionMetadata();
            var function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test", null, metadata, null, null, null, null);
            var attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "get")
            {
                Route = "products/{category}/{id?}"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);

            ScriptHost.ValidateFunction(function.Object, httpFunctions);
            Assert.Equal(1, httpFunctions.Count);
            Assert.True(httpFunctions.ContainsKey("test"));

            // add another for a completely different route
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test2", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "get")
            {
                Route = "/foo/bar/baz/"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            ScriptHost.ValidateFunction(function.Object, httpFunctions);
            Assert.Equal(2, httpFunctions.Count);
            Assert.True(httpFunctions.ContainsKey("test2"));

            // add another that varies from another only by http methods
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test3", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "put", "post")
            {
                Route = "/foo/bar/baz/"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            ScriptHost.ValidateFunction(function.Object, httpFunctions);
            Assert.Equal(3, httpFunctions.Count);
            Assert.True(httpFunctions.ContainsKey("test3"));

            // now try to add a function for the same route
            // where the http methods overlap
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test4", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute
            {
                Route = "/foo/bar/baz/"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                ScriptHost.ValidateFunction(function.Object, httpFunctions);
            });
            Assert.Equal("The route specified conflicts with the route defined by function 'test2'.", ex.Message);
            Assert.Equal(3, httpFunctions.Count);

            // try to add a route under reserved admin route
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test5", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute
            {
                Route = "admin/foo/bar"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            ex = Assert.Throws<InvalidOperationException>(() =>
            {
                ScriptHost.ValidateFunction(function.Object, httpFunctions);
            });
            Assert.Equal("The specified route conflicts with one or more built in routes.", ex.Message);

            // try to add a route under reserved runtime route
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test6", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute
            {
                Route = "runtime/foo/bar"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            ex = Assert.Throws<InvalidOperationException>(() =>
            {
                ScriptHost.ValidateFunction(function.Object, httpFunctions);
            });
            Assert.Equal("The specified route conflicts with one or more built in routes.", ex.Message);

            // verify that empty route is defaulted to function name
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test7", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute();
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            ScriptHost.ValidateFunction(function.Object, httpFunctions);
            Assert.Equal(4, httpFunctions.Count);
            Assert.True(httpFunctions.ContainsKey("test7"));
            Assert.Equal("test7", attribute.Route);
        }

        [Fact]
        public void HttpRoutesConflict_ReturnsExpectedResult()
        {
            var first = new HttpTriggerAttribute
            {
                Route = "foo/bar/baz"
            };
            var second = new HttpTriggerAttribute
            {
                Route = "foo/bar"
            };
            Assert.False(ScriptHost.HttpRoutesConflict(first, second));
            Assert.False(ScriptHost.HttpRoutesConflict(second, first));

            first = new HttpTriggerAttribute
            {
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerAttribute
            {
                Route = "foo/bar/baz"
            };
            Assert.True(ScriptHost.HttpRoutesConflict(first, second));
            Assert.True(ScriptHost.HttpRoutesConflict(second, first));

            // no conflict since methods do not intersect
            first = new HttpTriggerAttribute(AuthorizationLevel.Function, "get", "head")
            {
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerAttribute(AuthorizationLevel.Function, "post", "put")
            {
                Route = "foo/bar/baz"
            };
            Assert.False(ScriptHost.HttpRoutesConflict(first, second));
            Assert.False(ScriptHost.HttpRoutesConflict(second, first));

            first = new HttpTriggerAttribute(AuthorizationLevel.Function, "get", "head")
            {
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerAttribute
            {
                Route = "foo/bar/baz"
            };
            Assert.True(ScriptHost.HttpRoutesConflict(first, second));
            Assert.True(ScriptHost.HttpRoutesConflict(second, first));

            first = new HttpTriggerAttribute(AuthorizationLevel.Function, "get", "head", "put", "post")
            {
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerAttribute(AuthorizationLevel.Function, "put")
            {
                Route = "foo/bar/baz"
            };
            Assert.True(ScriptHost.HttpRoutesConflict(first, second));
            Assert.True(ScriptHost.HttpRoutesConflict(second, first));
        }

        [Fact]
        public void ValidateFunction_ThrowsOnDuplicateName()
        {
            var httpFunctions = new Dictionary<string, HttpTriggerAttribute>();
            var name = "test";

            // first add an http function
            var metadata = new FunctionMetadata();
            var function = new Mock<FunctionDescriptor>(MockBehavior.Strict, name, null, metadata, null, null, null, null);
            var attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "get");
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);

            ScriptHost.ValidateFunction(function.Object, httpFunctions);

            // add a proxy with same name
            metadata = new FunctionMetadata()
            {
                IsProxy = true
            };
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, name, null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute(AuthorizationLevel.Function, "get")
            {
                Route = "proxyRoute"
            };
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                ScriptHost.ValidateFunction(function.Object, httpFunctions);
            });

            Assert.Equal(string.Format($"The function or proxy name '{name}' must be unique within the function app.", name), ex.Message);
        }

        [Fact]
        public async Task IsFunction_ReturnsExpectedResult()
        {
            var host = TestHelpers.GetDefaultHost(o =>
            {
                o.ScriptPath = TestHelpers.FunctionsTestDirectory;
                o.LogPath = TestHelpers.GetHostLogFileDirectory().FullName;
            });
            await host.StartAsync();
            var scriptHost = host.GetScriptHost();

            var parameters = new Collection<ParameterDescriptor>();
            parameters.Add(new ParameterDescriptor("param1", typeof(string)));
            var metadata = new FunctionMetadata();
            var invoker = new TestInvoker();
            var function = new FunctionDescriptor("TestFunction", invoker, metadata, parameters, null, null, null);
            scriptHost.Functions.Add(function);

            var errors = new Collection<string>();
            errors.Add("A really really bad error!");
            scriptHost.FunctionErrors.Add("ErrorFunction", errors);

            Assert.True(scriptHost.IsFunction("TestFunction"));
            Assert.True(scriptHost.IsFunction("ErrorFunction"));
            Assert.False(scriptHost.IsFunction("DoesNotExist"));
            Assert.False(scriptHost.IsFunction(string.Empty));
            Assert.False(scriptHost.IsFunction(null));
        }

        [Fact]
        public async Task Initialize_LogsWarningForExplicitlySetHostId()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests_Initialize_LogsWarningForExplicitlySetHostId");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // Set id in the host.json
            string hostJsonContent = @"
            {
                'version': '2.0'
            }";

            File.WriteAllText(Path.Combine(rootPath, "host.json"), hostJsonContent);

            var config = new ScriptJobHostOptions()
            {
                RootScriptPath = rootPath
            };

            var host = new HostBuilder()
                .ConfigureAppConfiguration(b =>
                {
                    b.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationSectionNames.HostIdPath, "foobar" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost(_ => { },
                options =>
                {
                    options.ScriptPath = rootPath;
                    options.LogPath = Path.GetTempPath();
                },
                runStartupHostedServices: true,
                rootServices =>
                {
                    rootServices.AddSingleton<ILoggerFactory>(loggerFactory);
                })
                .ConfigureServices(s =>
                {
                    s.AddSingleton<ILoggerFactory>(loggerFactory);
                })
                .Build();

            await host.StartAsync();

            var idProvider = host.Services.GetService<IHostIdProvider>();
            string hostId = await idProvider.GetHostIdAsync(CancellationToken.None);

            await host.StopAsync();
            host.Dispose();

            Assert.Matches("foobar", hostId);

            // We should have a warning for host id in the start up logger
            var logger = loggerProvider.CreatedLoggers.First(x => x.Category == "Host.Startup");
            Assert.Single(logger.GetLogMessages(), x => x.FormattedMessage.Contains("Host id explicitly set in configuration."));
        }

        public class AssemblyMock : Assembly
        {
            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                return new Attribute[] { };
            }
        }

        public class TestFixture : IAsyncLifetime
        {
            public ScriptHost ScriptHost => Host.GetScriptHost();

            public IHost Host { get; set; }

            public async Task DisposeAsync()
            {
                await Host.StopAsync();
                Host.Dispose();
            }

            public async Task InitializeAsync()
            {
                Directory.CreateDirectory(TestHelpers.FunctionsTestDirectory);
                var environment = new Mock<IScriptJobHostEnvironment>();
                var eventManager = new Mock<IScriptEventManager>();

                Host = new HostBuilder()
                    .ConfigureDefaultTestWebScriptHost(o =>
                    {
                        o.ScriptPath = TestHelpers.FunctionsTestDirectory;
                        o.LogPath = TestHelpers.GetHostLogFileDirectory().FullName;
                    })
                    .Build();

                await ScriptHost.InitializeAsync();
            }
        }
    }
}