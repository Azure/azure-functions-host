// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostTests : IClassFixture<ScriptHostTests.TestFixture>
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";
        private readonly TestFixture _fixture;
        private readonly ScriptSettingsManager _settingsManager;

        private readonly ILoggerFactory _loggerFactory = new LoggerFactory();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public ScriptHostTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;

            _loggerFactory.AddProvider(_loggerProvider);
        }

        [Theory]
        [InlineData(@"C:\Functions\Scripts\Shared\Test.csx", "Shared")]
        [InlineData(@"C:\Functions\Scripts\Shared\Sub1\Sub2\Test.csx", "Shared")]
        [InlineData(@"C:\Functions\Scripts\Shared", "Shared")]
        public static void GetRelativeDirectory_ReturnsExpectedDirectoryName(string path, string expected)
        {
            Assert.Equal(expected, ScriptHost.GetRelativeDirectory(path, @"C:\Functions\Scripts"));
        }

        [Fact]
        public void ReadFunctionMetadata_Succeeds()
        {
            var config = new ScriptHostConfiguration
            {
                RootScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\..\sample")
            };

            var functionErrors = new Dictionary<string, Collection<string>>();
            var functionDirectories = Directory.EnumerateDirectories(config.RootScriptPath);
            var metadata = ScriptHost.ReadFunctionsMetadata(functionDirectories, null, functionErrors);
            Assert.Equal(41, metadata.Count);
        }

        [Fact]
        public async Task OnDebugModeFileChanged_TriggeredWhenDebugFileUpdated()
        {
            ScriptHost host = _fixture.Host;
            string debugSentinelFilePath = Path.Combine(host.ScriptConfig.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);

            if (!File.Exists(debugSentinelFilePath))
            {
                File.WriteAllText(debugSentinelFilePath, string.Empty);
            }

            host.LastDebugNotify = DateTime.MinValue;
            Assert.False(host.InDebugMode, $"Expected InDebugMode to be false. Now: {DateTime.UtcNow}; Sentinel LastWriteTime: {File.GetLastWriteTimeUtc(debugSentinelFilePath)}; LastDebugNotify: {host.LastDebugNotify}.");

            // verify that our file watcher for the debug sentinel file is configured
            // properly by touching the file and ensuring that our host goes into
            // debug mode
            File.SetLastWriteTimeUtc(debugSentinelFilePath, DateTime.UtcNow);

            await TestHelpers.Await(() =>
            {
                return host.InDebugMode;
            });

            Assert.True(host.InDebugMode);
        }

        [Fact]
        public void InDebugMode_ReturnsExpectedValue()
        {
            ScriptHost host = _fixture.Host;

            host.LastDebugNotify = DateTime.MinValue;
            Assert.False(host.InDebugMode);

            host.LastDebugNotify = DateTime.UtcNow - TimeSpan.FromSeconds(60 * ScriptHost.DebugModeTimeoutMinutes);
            Assert.False(host.InDebugMode);

            host.LastDebugNotify = DateTime.UtcNow - TimeSpan.FromSeconds(60 * (ScriptHost.DebugModeTimeoutMinutes - 1));
            Assert.True(host.InDebugMode);
        }

        [Fact]
        public void FileLoggingEnabled_ReturnsExpectedValue()
        {
            ScriptHost host = _fixture.Host;

            host.ScriptConfig.FileLoggingMode = FileLoggingMode.DebugOnly;
            host.LastDebugNotify = DateTime.MinValue;
            Assert.False(host.FileLoggingEnabled);
            host.NotifyDebug();
            Assert.True(host.FileLoggingEnabled);

            host.ScriptConfig.FileLoggingMode = FileLoggingMode.Never;
            Assert.False(host.FileLoggingEnabled);

            host.ScriptConfig.FileLoggingMode = FileLoggingMode.Always;
            Assert.True(host.FileLoggingEnabled);
            host.LastDebugNotify = DateTime.MinValue;
            Assert.True(host.FileLoggingEnabled);
        }

        [Fact]
        public void NotifyDebug_UpdatesDebugMarkerFileAndTimestamp()
        {
            ScriptHost host = _fixture.Host;

            string debugSentinelFileName = Path.Combine(host.ScriptConfig.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);
            File.Delete(debugSentinelFileName);
            host.LastDebugNotify = DateTime.MinValue;

            Assert.False(host.InDebugMode);

            DateTime lastDebugNotify = host.LastDebugNotify;
            host.NotifyDebug();
            Assert.True(host.InDebugMode);
            Assert.True(File.Exists(debugSentinelFileName));
            string text = File.ReadAllText(debugSentinelFileName);
            Assert.Equal("This is a system managed marker file used to control runtime debug mode behavior.", text);
            Assert.True(host.LastDebugNotify > lastDebugNotify);

            Thread.Sleep(500);

            DateTime lastModified = File.GetLastWriteTime(debugSentinelFileName);
            lastDebugNotify = host.LastDebugNotify;
            host.NotifyDebug();
            Assert.True(host.InDebugMode);
            Assert.True(File.Exists(debugSentinelFileName));
            Assert.True(File.GetLastWriteTime(debugSentinelFileName) > lastModified);
            Assert.True(host.LastDebugNotify > lastDebugNotify);
        }

        [Fact]
        public void NotifyDebug_HandlesExceptions()
        {
            ScriptHost host = _fixture.Host;
            string debugSentinelFileName = Path.Combine(host.ScriptConfig.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);

            try
            {
                host.NotifyDebug();
                Assert.True(host.InDebugMode);

                var attributes = File.GetAttributes(debugSentinelFileName);
                attributes |= FileAttributes.ReadOnly;
                File.SetAttributes(debugSentinelFileName, attributes);
                Assert.True(host.InDebugMode);

                host.NotifyDebug();
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

        [Theory]
        [InlineData("QUEUETriggER.py")]
        [InlineData("queueTrigger.py")]
        public void DeterminePrimaryScriptFile_MultipleFiles_SourceFileSpecified(string scriptFileName)
        {
            JObject functionConfig = new JObject()
            {
                { "scriptFile", scriptFileName }
            };

            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };

            var fileSystem = new MockFileSystem(files);

            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\queueTrigger.py", scriptFile, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_RelativeSourceFileSpecified()
        {
            JObject functionConfig = new JObject()
            {
                { "scriptFile", @"..\shared\queuetrigger.py" }
            };

            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\shared\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };

            var fileSystem = new MockFileSystem(files);

            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\shared\queueTrigger.py", scriptFile, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_ConfigTrumpsConvention()
        {
            JObject functionConfig = new JObject()
            {
                { "scriptFile", "queueTrigger.py" }
            };
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\run.py", new MockFileData(string.Empty) },
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);

            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\queueTrigger.py", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_NoClearPrimary_ReturnsNull()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\foo.py", new MockFileData(string.Empty) },
                { @"c:\functions\queueTrigger.py", new MockFileData(string.Empty) },
                { @"c:\functions\helper.py", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            Assert.Throws<ScriptConfigurationException>(() => ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_NoFiles_ReturnsNull()
        {
            var functionConfig = new JObject();
            string[] functionFiles = new string[0];
            var fileSystem = new MockFileSystem();
            fileSystem.AddDirectory(@"c:\functions");
            Assert.Throws<ScriptConfigurationException>(() => ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunFilePresent()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\Run.csx", new MockFileData(string.Empty) },
                { @"c:\functions\Helper.csx", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_SingleFile()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\Run.csx", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunTrumpsIndex()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\run.js", new MockFileData(string.Empty) },
                { @"c:\functions\index.js", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\run.js", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_IndexFilePresent()
        {
            var functionConfig = new JObject();
            var files = new Dictionary<string, MockFileData>
            {
                { @"c:\functions\index.js", new MockFileData(string.Empty) },
                { @"c:\functions\test.txt", new MockFileData(string.Empty) }
            };
            var fileSystem = new MockFileSystem(files);
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem);
            Assert.Equal(@"c:\functions\index.js", scriptFile);
        }

        [Fact]
        public void Create_InvalidHostJson_ThrowsInformativeException()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Invalid");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();
            var host = new ScriptHost(environment.Object, eventManager.Object, scriptConfig, _settingsManager);

            var ex = Assert.Throws<FormatException>(() =>
            {
                host.Initialize();
            });

            Assert.Equal(string.Format("Unable to parse {0} file.", ScriptConstants.HostMetadataFileName), ex.Message);
            Assert.Equal("Invalid property identifier character: ~. Path '', line 2, position 4.", ex.InnerException.Message);
        }

        [Theory]
        [InlineData("host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
        [InlineData("function.test")]
        [InlineData("function0.1")]
        public void Initialize_InvalidFunctionNames_DoesNotCreateFunctionAndLogsFailure(string functionName)
        {
            string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string invalidFunctionNamePath = Path.Combine(rootPath, functionName);
            try
            {
                Directory.CreateDirectory(invalidFunctionNamePath);

                JObject config = new JObject();
                config["id"] = ID;

                File.WriteAllText(Path.Combine(rootPath, ScriptConstants.HostMetadataFileName), config.ToString());
                File.WriteAllText(Path.Combine(invalidFunctionNamePath, ScriptConstants.FunctionMetadataFileName), string.Empty);

                ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration()
                {
                    RootScriptPath = rootPath
                };
                var environment = new Mock<IScriptHostEnvironment>();
                var eventManager = new Mock<IScriptEventManager>();

                var scriptHost = new ScriptHost(environment.Object, eventManager.Object, scriptConfig, _settingsManager);
                scriptHost.Initialize();

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

        [Fact]
        public void ApplyConfiguration_TopLevel()
        {
            JObject config = new JObject();
            config["id"] = ID;
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.Equal(ID, scriptConfig.HostConfig.HostId);
        }

        // TODO: Move this test into a new WebJobsCoreScriptBindingProvider class since
        // the functionality moved. Also add tests for the ServiceBus config, etc.
        [Fact]
        public void ApplyConfiguration_Queues()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject queuesConfig = new JObject();
            config["queues"] = queuesConfig;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            scriptConfig.HostConfig.HostConfigMetadata = config;

            new JobHost(scriptConfig.HostConfig).CreateMetadataProvider(); // will cause extensions to initialize and consume config metadata.

            Assert.Equal(60 * 1000, scriptConfig.HostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(16, scriptConfig.HostConfig.Queues.BatchSize);
            Assert.Equal(5, scriptConfig.HostConfig.Queues.MaxDequeueCount);
            Assert.Equal(8, scriptConfig.HostConfig.Queues.NewBatchThreshold);
            Assert.Equal(TimeSpan.Zero, scriptConfig.HostConfig.Queues.VisibilityTimeout);

            queuesConfig["maxPollingInterval"] = 5000;
            queuesConfig["batchSize"] = 17;
            queuesConfig["maxDequeueCount"] = 3;
            queuesConfig["newBatchThreshold"] = 123;
            queuesConfig["visibilityTimeout"] = "00:00:30";

            scriptConfig = new ScriptHostConfiguration();
            scriptConfig.HostConfig.HostConfigMetadata = config;
            new JobHost(scriptConfig.HostConfig).CreateMetadataProvider(); // will cause extensions to initialize and consume config metadata.

            Assert.Equal(5000, scriptConfig.HostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(17, scriptConfig.HostConfig.Queues.BatchSize);
            Assert.Equal(3, scriptConfig.HostConfig.Queues.MaxDequeueCount);
            Assert.Equal(123, scriptConfig.HostConfig.Queues.NewBatchThreshold);
            Assert.Equal(TimeSpan.FromSeconds(30), scriptConfig.HostConfig.Queues.VisibilityTimeout);
        }

        [Fact]
        public void ApplyConfiguration_Http()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject http = new JObject();
            config["http"] = http;

            JobHostConfiguration hostConfig = new JobHostConfiguration();
            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            provider.Initialize();

            IExtensionRegistry extensions = hostConfig.GetService<IExtensionRegistry>();
            var httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();

            Assert.Equal(HttpExtensionConstants.DefaultRoutePrefix, httpConfig.RoutePrefix);
            Assert.Equal(false, httpConfig.DynamicThrottlesEnabled);
            Assert.Equal(DataflowBlockOptions.Unbounded, httpConfig.MaxConcurrentRequests);
            Assert.Equal(DataflowBlockOptions.Unbounded, httpConfig.MaxOutstandingRequests);

            http["routePrefix"] = "myprefix";
            http["dynamicThrottlesEnabled"] = true;
            http["maxConcurrentRequests"] = 5;
            http["maxOutstandingRequests"] = 10;

            hostConfig = new JobHostConfiguration();
            provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            provider.Initialize();

            extensions = hostConfig.GetService<IExtensionRegistry>();
            httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();

            Assert.Equal("myprefix", httpConfig.RoutePrefix);
            Assert.True(httpConfig.DynamicThrottlesEnabled);
            Assert.Equal(5, httpConfig.MaxConcurrentRequests);
            Assert.Equal(10, httpConfig.MaxOutstandingRequests);
        }

        [Fact]
        public void ApplyConfiguration_Blobs()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject blobsConfig = new JObject();
            config["blobs"] = blobsConfig;

            JobHostConfiguration hostConfig = new JobHostConfiguration();

            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            provider.Initialize();

            Assert.True(hostConfig.Blobs.CentralizedPoisonQueue);

            blobsConfig["centralizedPoisonQueue"] = false;

            provider = new WebJobsCoreScriptBindingProvider(hostConfig, config, null);
            provider.Initialize();

            Assert.False(hostConfig.Blobs.CentralizedPoisonQueue);
        }

        [Fact]
        public void ApplyConfiguration_Singleton()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject singleton = new JObject();
            config["singleton"] = singleton;
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.Equal(ID, scriptConfig.HostConfig.HostId);
            Assert.Equal(15, scriptConfig.HostConfig.Singleton.LockPeriod.TotalSeconds);
            Assert.Equal(1, scriptConfig.HostConfig.Singleton.ListenerLockPeriod.TotalMinutes);
            Assert.Equal(1, scriptConfig.HostConfig.Singleton.ListenerLockRecoveryPollingInterval.TotalMinutes);
            Assert.Equal(TimeSpan.MaxValue, scriptConfig.HostConfig.Singleton.LockAcquisitionTimeout);
            Assert.Equal(5, scriptConfig.HostConfig.Singleton.LockAcquisitionPollingInterval.TotalSeconds);

            singleton["lockPeriod"] = "00:00:17";
            singleton["listenerLockPeriod"] = "00:00:22";
            singleton["listenerLockRecoveryPollingInterval"] = "00:00:33";
            singleton["lockAcquisitionTimeout"] = "00:05:00";
            singleton["lockAcquisitionPollingInterval"] = "00:00:08";

            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.Equal(17, scriptConfig.HostConfig.Singleton.LockPeriod.TotalSeconds);
            Assert.Equal(22, scriptConfig.HostConfig.Singleton.ListenerLockPeriod.TotalSeconds);
            Assert.Equal(33, scriptConfig.HostConfig.Singleton.ListenerLockRecoveryPollingInterval.TotalSeconds);
            Assert.Equal(5, scriptConfig.HostConfig.Singleton.LockAcquisitionTimeout.TotalMinutes);
            Assert.Equal(8, scriptConfig.HostConfig.Singleton.LockAcquisitionPollingInterval.TotalSeconds);
        }

        [Fact]
        public void ApplyConfiguration_FileWatching()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            Assert.True(scriptConfig.FileWatchingEnabled);

            scriptConfig = new ScriptHostConfiguration();
            config["fileWatchingEnabled"] = new JValue(true);
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.True(scriptConfig.FileWatchingEnabled);
            Assert.Equal(1, scriptConfig.WatchDirectories.Count);
            Assert.Equal("node_modules", scriptConfig.WatchDirectories.ElementAt(0));

            scriptConfig = new ScriptHostConfiguration();
            config["fileWatchingEnabled"] = new JValue(false);
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.False(scriptConfig.FileWatchingEnabled);

            scriptConfig = new ScriptHostConfiguration();
            config["fileWatchingEnabled"] = new JValue(true);
            config["watchDirectories"] = new JArray("Shared", "Tools");
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.True(scriptConfig.FileWatchingEnabled);
            Assert.Equal(3, scriptConfig.WatchDirectories.Count);
            Assert.Equal("node_modules", scriptConfig.WatchDirectories.ElementAt(0));
            Assert.Equal("Shared", scriptConfig.WatchDirectories.ElementAt(1));
            Assert.Equal("Tools", scriptConfig.WatchDirectories.ElementAt(2));
        }

        [Fact]
        public void ApplyConfiguration_AppliesFunctionsFilter()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            Assert.Null(scriptConfig.Functions);

            config["functions"] = new JArray("Function1", "Function2");

            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(2, scriptConfig.Functions.Count);
            Assert.Equal("Function1", scriptConfig.Functions.ElementAt(0));
            Assert.Equal("Function2", scriptConfig.Functions.ElementAt(1));
        }

        [Fact]
        public void ApplyConfiguration_ClearsFunctionsFilter()
        {
            // A previous bug wouldn't properly clear the filter if you removed it.
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            Assert.Null(scriptConfig.Functions);

            config["functions"] = new JArray("Function1", "Function2");

            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(2, scriptConfig.Functions.Count);
            Assert.Equal("Function1", scriptConfig.Functions.ElementAt(0));
            Assert.Equal("Function2", scriptConfig.Functions.ElementAt(1));

            config.Remove("functions");

            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.Null(scriptConfig.Functions);
        }

        [Fact]
        public void ApplyConfiguration_AppliesTimeout()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            Assert.Null(scriptConfig.FunctionTimeout);

            config["functionTimeout"] = "00:00:30";

            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(TimeSpan.FromSeconds(30), scriptConfig.FunctionTimeout);
        }

        [Fact]
        public void ApplyConfiguration_TimeoutDefaultsNull_IfNotDynamic()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Null(scriptConfig.FunctionTimeout);
        }

        [Fact]
        public void ApplyConfiguration_AppliesDefaultTimeout_IfDynamic()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            try
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");
                ScriptHost.ApplyConfiguration(config, scriptConfig);
                Assert.Equal(ScriptHost.DefaultFunctionTimeout, scriptConfig.FunctionTimeout);

                var timeoutConfig = scriptConfig.HostConfig.FunctionTimeout;
                Assert.NotNull(timeoutConfig);
                Assert.True(timeoutConfig.ThrowOnTimeout);
                Assert.Equal(scriptConfig.FunctionTimeout.Value, timeoutConfig.Timeout);
            }
            finally
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, null);
            }
        }

        [Fact]
        public void ApplyConfiguration_NoTimeoutLimits_IfNotDynamic()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            var timeout = ScriptHost.MaxFunctionTimeout + TimeSpan.FromSeconds(1);
            config["functionTimeout"] = timeout.ToString();
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(timeout, scriptConfig.FunctionTimeout);

            timeout = ScriptHost.MinFunctionTimeout - TimeSpan.FromSeconds(1);
            config["functionTimeout"] = timeout.ToString();
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(timeout, scriptConfig.FunctionTimeout);
        }

        [Fact]
        public void ApplyConfiguration_AppliesTimeoutLimits_IfDynamic()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            try
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

                config["functionTimeout"] = (ScriptHost.MaxFunctionTimeout + TimeSpan.FromSeconds(1)).ToString();
                var ex = Assert.Throws<ArgumentException>(() => ScriptHost.ApplyConfiguration(config, scriptConfig));
                var expectedMessage = "FunctionTimeout must be between 00:00:01 and 00:10:00.";
                Assert.Equal(expectedMessage, ex.Message);

                config["functionTimeout"] = (ScriptHost.MinFunctionTimeout - TimeSpan.FromSeconds(1)).ToString();
                Assert.Throws<ArgumentException>(() => ScriptHost.ApplyConfiguration(config, scriptConfig));
                Assert.Equal(expectedMessage, ex.Message);
            }
            finally
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, null);
            }
        }

        [Fact]
        public void ApplyApplicationInsightsConfig_SamplingDisabled_CreatesNullSettings()
        {
            JObject config = JObject.Parse(@"
             {
                 'applicationInsights': {
                     'sampling': {
                         'isEnabled': false
                     }
                 }
             }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyApplicationInsightsConfig(config, scriptConfig);

            Assert.Null(scriptConfig.ApplicationInsightsSamplingSettings);
        }

        [Fact]
        public void ApplyApplicationInsightsConfig_SamplingDisabled_IgnoresOtherSettings()
        {
            JObject config = JObject.Parse(@"
             {
                 'applicationInsights': {
                     'sampling': {
                         'isEnabled': false,
                         'maxTelemetryItemsPerSecond': 25
                     }
                 }
             }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyApplicationInsightsConfig(config, scriptConfig);

            Assert.Null(scriptConfig.ApplicationInsightsSamplingSettings);
        }

        [Fact]
        public void ApplyApplicationInsightsConfig_SamplingEnabled_CreatesDefaultSettings()
        {
            JObject config = JObject.Parse(@"
             {
                 'applicationInsights': {
                     'sampling': {
                         'isEnabled': true
                     }
                 }
             }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyApplicationInsightsConfig(config, scriptConfig);

            Assert.NotNull(scriptConfig.ApplicationInsightsSamplingSettings);
            Assert.Equal(5, scriptConfig.ApplicationInsightsSamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void ApplyApplicationInsightsConfig_Sets_MaxTelemetryItemsPerSecond()
        {
            JObject config = JObject.Parse(@"
             {
                 'applicationInsights': {
                     'sampling': {
                         'maxTelemetryItemsPerSecond': 25
                     }
                 }
             }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyApplicationInsightsConfig(config, scriptConfig);

            Assert.NotNull(scriptConfig.ApplicationInsightsSamplingSettings);
            Assert.Equal(25, scriptConfig.ApplicationInsightsSamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void ApplyHostHealthMonitorConfig_AppliesExpectedSettings()
        {
            JObject config = JObject.Parse("{ }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.True(scriptConfig.HostHealthMonitor.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(10), scriptConfig.HostHealthMonitor.HealthCheckInterval);
            Assert.Equal(TimeSpan.FromMinutes(2), scriptConfig.HostHealthMonitor.HealthCheckWindow);
            Assert.Equal(6, scriptConfig.HostHealthMonitor.HealthCheckThreshold);
            Assert.Equal(HostHealthMonitorConfiguration.DefaultCounterThreshold, scriptConfig.HostHealthMonitor.CounterThreshold);

            // now set custom configuration and verify
            config = JObject.Parse(@"
             {
                 'healthMonitor': {
                     'enabled': false
                 }
             }");
            scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.False(scriptConfig.HostHealthMonitor.Enabled);

            config = JObject.Parse(@"
             {
                 'healthMonitor': {
                     'enabled': true,
                     'healthCheckInterval': '00:00:07',
                     'healthCheckWindow': '00:07:00',
                     'healthCheckThreshold': 77,
                     'counterThreshold': 0.77
                 }

             }");
            scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.True(scriptConfig.HostHealthMonitor.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(7), scriptConfig.HostHealthMonitor.HealthCheckInterval);
            Assert.Equal(TimeSpan.FromMinutes(7), scriptConfig.HostHealthMonitor.HealthCheckWindow);
            Assert.Equal(77, scriptConfig.HostHealthMonitor.HealthCheckThreshold);
            Assert.Equal(0.77F, scriptConfig.HostHealthMonitor.CounterThreshold);
        }

        [Fact]
        public void ApplyApplicationInsightsConfig_NoSettings_CreatesDefaultSettings()
        {
            JObject config = JObject.Parse("{ }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyApplicationInsightsConfig(config, scriptConfig);

            Assert.NotNull(scriptConfig.ApplicationInsightsSamplingSettings);
            Assert.Equal(5, scriptConfig.ApplicationInsightsSamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void ApplyLoggerConfig_Sets_DefaultLevel()
        {
            JObject config = JObject.Parse(@"
            {
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'Debug'
                    }
                }
            }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyLoggerConfig(config, scriptConfig);

            Assert.NotNull(scriptConfig.LogFilter);
            Assert.Equal(LogLevel.Debug, scriptConfig.LogFilter.DefaultLevel);
            Assert.Empty(scriptConfig.LogFilter.CategoryLevels);
        }

        [Fact]
        public void ApplyLoggerConfig_Sets_CategoryLevels()
        {
            JObject config = JObject.Parse(@"
            {
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'Trace',
                        'categoryLevels': {
                            'Host.General': 'Information',
                            'Host.SomethingElse': 'Debug',
                            'Some.Other.Category': 'None'
                        }
                    }
                }
            }");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyLoggerConfig(config, scriptConfig);

            Assert.NotNull(scriptConfig.LogFilter);
            Assert.Equal(LogLevel.Trace, scriptConfig.LogFilter.DefaultLevel);
            Assert.Equal(3, scriptConfig.LogFilter.CategoryLevels.Count);
            Assert.Equal(LogLevel.Information, scriptConfig.LogFilter.CategoryLevels["Host.General"]);
            Assert.Equal(LogLevel.Debug, scriptConfig.LogFilter.CategoryLevels["Host.SomethingElse"]);
            Assert.Equal(LogLevel.None, scriptConfig.LogFilter.CategoryLevels["Some.Other.Category"]);
        }

        [Fact]
        public void ApplyLoggerConfig_DefaultsToInfo()
        {
            JObject config = JObject.Parse("{}");

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            ScriptHost.ApplyLoggerConfig(config, scriptConfig);

            Assert.NotNull(scriptConfig.LogFilter);
            Assert.Equal(LogLevel.Information, scriptConfig.LogFilter.DefaultLevel);
            Assert.Empty(scriptConfig.LogFilter.CategoryLevels);
        }

        [Fact]
        public void Initialize_AppliesLoggerConfig()
        {
            TestLoggerProvider loggerProvider = null;
            var loggerFactoryHookMock = new Mock<ILoggerProviderFactory>(MockBehavior.Strict);
            loggerFactoryHookMock
                .Setup(m => m.CreateLoggerProviders(It.IsAny<string>(), It.IsAny<ScriptHostConfiguration>(), It.IsAny<ScriptSettingsManager>(), It.IsAny<Func<bool>>(), It.IsAny<Func<bool>>()))
                .Returns<string, ScriptHostConfiguration, ScriptSettingsManager, Func<bool>, Func<bool>>((instanceId, scriptConfig, settingsManager, fileLoggingEnabled, isPrimary) =>
                 {
                     loggerProvider = new TestLoggerProvider(scriptConfig.LogFilter.Filter);
                     return new[] { loggerProvider };
                 });

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // Turn off all logging. We shouldn't see any output.
            string hostJsonContent = @"
            {
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'None'
                    }
                }
            }";
            File.WriteAllText(Path.Combine(rootPath, "host.json"), hostJsonContent);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            config.HostConfig.HostId = ID;
            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();

            var host = new ScriptHost(environment.Object, eventManager.Object, config, null, loggerFactoryHookMock.Object);
            host.Initialize();

            // We shouldn't have any log messages
            foreach (var logger in loggerProvider.CreatedLoggers)
            {
                Assert.Empty(logger.GetLogMessages());
            }
        }

        [Fact]
        public void Initialize_Sanitizes_HostJsonLog()
        {
            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            var loggerProviderFactory = new TestLoggerProviderFactory(loggerProvider, includeDefaultLoggerProviders: false);

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // Turn off all logging. We shouldn't see any output.
            string hostJsonContent = @"
            {
                'functionTimeout': '00:05:00',
                'functions': [ 'FunctionA', 'FunctionB' ],
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'Information'
                    }
                },
                'Values': {
                    'MyCustomValue': 'abc'
                }
            }";

            File.WriteAllText(Path.Combine(rootPath, "host.json"), hostJsonContent);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            config.HostConfig.HostId = ID;
            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();

            var host = new ScriptHost(environment.Object, eventManager.Object, config, null, loggerProviderFactory, null);
            host.Initialize();

            string hostJsonSanitized = @"
            {
                'functionTimeout': '00:05:00',
                'functions': [ 'FunctionA', 'FunctionB' ],
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'Information'
                    }
                }
            }";

            // for formatting
            var hostJson = JObject.Parse(hostJsonSanitized);

            var logger = loggerProvider.CreatedLoggers.Single(l => l.Category == LogCategories.Startup);
            var logMessage = logger.GetLogMessages().Single(l => l.FormattedMessage.StartsWith("Host configuration file read")).FormattedMessage;
            Assert.Equal($"Host configuration file read:{Environment.NewLine}{hostJson}", logMessage);
        }

        [Fact]
        public void ConfigureLoggerFactory_Default()
        {
            var config = new ScriptHostConfiguration();
            var loggerFactoryMock = new Mock<ILoggerFactory>(MockBehavior.Loose);
            loggerFactoryMock.Setup(x => x.AddProvider(It.IsAny<ILoggerProvider>()));
            var loggerFactory = loggerFactoryMock.Object;
            config.HostConfig.LoggerFactory = loggerFactory;

            // Make sure no App Insights is configured
            var settingsManager = ScriptSettingsManager.Instance;
            settingsManager.ApplicationInsightsInstrumentationKey = null;

            var metricsLogger = new TestMetricsLogger();
            config.HostConfig.AddService<IMetricsLogger>(metricsLogger);

            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();

            ILoggerProviderFactory builder = new DefaultLoggerProviderFactory();

            ScriptHost.ConfigureLoggerFactory(Guid.NewGuid().ToString(), loggerFactory, config, settingsManager, builder, () => true, () => true, (ex) => { });

            loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<FunctionFileLoggerProvider>()), Times.AtLeastOnce());
            Assert.Equal(1, metricsLogger.LoggedEvents.Count);
            Assert.Equal(MetricEventNames.ApplicationInsightsDisabled, metricsLogger.LoggedEvents[0]);
        }

        [Theory]
        [InlineData("always")]
        [InlineData("never")]
        [InlineData("")]
        [InlineData(null)]
        public void ConfigureLoggerFactory_ApplicationInsights(string consoleLoggingEnabled)
        {
            var config = new ScriptHostConfiguration();
            var loggerFactoryMock = new Mock<ILoggerFactory>(MockBehavior.Loose);
            loggerFactoryMock.Setup(x => x.AddProvider(It.IsAny<ILoggerProvider>()));
            var loggerFactory = loggerFactoryMock.Object;
            config.HostConfig.LoggerFactory = loggerFactory;

            // Make sure no App Insights is configured
            var settingsManager = ScriptSettingsManager.Instance;
            settingsManager.SetSetting("host:logger:consoleLoggingMode", consoleLoggingEnabled);
            settingsManager.ApplicationInsightsInstrumentationKey = "Some_Instrumentation_Key";

            var metricsLogger = new TestMetricsLogger();
            config.HostConfig.AddService<IMetricsLogger>(metricsLogger);

            ILoggerProviderFactory builder = new DefaultLoggerProviderFactory();

            ScriptHost.ConfigureLoggerFactory(Guid.NewGuid().ToString(), loggerFactory, config, settingsManager, builder, () => true, () => true, (ex) => { });

            if (consoleLoggingEnabled == "always")
            {
                loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<ILoggerProvider>()), Times.Exactly(5));
                loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<ConsoleLoggerProvider>()), Times.Once());
            }
            else
            {
                loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<ILoggerProvider>()), Times.Exactly(4));
            }

            loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<FunctionFileLoggerProvider>()), Times.Once());
            loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<HostFileLoggerProvider>()), Times.Once());
            loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<ApplicationInsightsLoggerProvider>()), Times.Once());
            loggerFactoryMock.Verify(x => x.AddProvider(It.IsAny<HostErrorLoggerProvider>()), Times.Once());

            Assert.Equal(1, metricsLogger.LoggedEvents.Count);
            Assert.Equal(MetricEventNames.ApplicationInsightsEnabled, metricsLogger.LoggedEvents[0]);
        }

        [Fact]
        public void DefaultLoggerFactory_BeginScope()
        {
            var config = new ScriptHostConfiguration();

            var channel = new TestTelemetryChannel();

            ILoggerFactory loggerFactory = new LoggerFactory();

            var settingsManager = ScriptSettingsManager.Instance;
            settingsManager.ApplicationInsightsInstrumentationKey = TestChannelLoggerProviderFactory.ApplicationInsightsKey;

            var builder = new TestChannelLoggerProviderFactory(channel);

            ScriptHost.ConfigureLoggerFactory(Guid.NewGuid().ToString(), loggerFactory, config, settingsManager, builder, () => true, () => true, (ex) => { });

            // Create a logger and try out the configured factory. We need to pretend that it is coming from a
            // function, so set the function name and the category appropriately.
            var logger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory("Test"));

            using (logger.BeginScope(new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyFunctionNameKey] = "Test"
            }))
            {
                // Now log as if from within a function.

                // Test that both dictionaries and structured logs work as state
                // and that nesting works as expected.
                using (logger.BeginScope("{customKey1}", "customValue1"))
                {
                    logger.LogInformation("1");

                    using (logger.BeginScope(new Dictionary<string, object>
                    {
                        ["customKey2"] = "customValue2"
                    }))
                    {
                        logger.LogInformation("2");
                    }

                    logger.LogInformation("3");
                }

                using (logger.BeginScope("should not throw"))
                {
                    logger.LogInformation("4");
                }
            }

            Assert.Equal(4, channel.Telemetries.Count);

            var traces = channel.Telemetries.Cast<TraceTelemetry>().OrderBy(t => t.Message).ToArray();

            // Every telemetry will have {originalFormat}, Category, Level, but we validate those elsewhere.
            // We're only interested in the custom properties.
            Assert.Equal("1", traces[0].Message);
            Assert.Equal(4, traces[0].Properties.Count);
            Assert.Equal("customValue1", traces[0].Properties["prop__customKey1"]);

            Assert.Equal("2", traces[1].Message);
            Assert.Equal(5, traces[1].Properties.Count);
            Assert.Equal("customValue1", traces[1].Properties["prop__customKey1"]);
            Assert.Equal("customValue2", traces[1].Properties["prop__customKey2"]);

            Assert.Equal("3", traces[2].Message);
            Assert.Equal(4, traces[2].Properties.Count);
            Assert.Equal("customValue1", traces[2].Properties["prop__customKey1"]);

            Assert.Equal("4", traces[3].Message);
            Assert.Equal(3, traces[3].Properties.Count);
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
            FunctionDescriptor functionResult = null;
            bool result = ScriptHost.TryGetFunctionFromException(functions, exception, out functionResult);
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
        [InlineData("")]
        [InlineData("host")]
        [InlineData("Host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
        [InlineData("function.test")]
        [InlineData("function0.1")]
        public void ValidateFunctionName_ThrowsOnInvalidName(string functionName)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                ScriptHost.ValidateName(functionName);
            });

            Assert.Equal(string.Format("'{0}' is not a valid function name.", functionName), ex.Message);
        }

        [Theory]
        [InlineData("myproxy")]
        [InlineData("my proxy")]
        [InlineData("my proxy %")]
        public void UpdateProxyName(string proxyName)
        {
            Assert.Equal("myproxy", ScriptHost.NormalizeProxyName(proxyName));
        }

        [Theory]
        [InlineData("testwithhost")]
        [InlineData("hosts")]
        [InlineData("myfunction")]
        [InlineData("myfunction-test")]
        [InlineData("myfunction_test")]
        public void ValidateFunctionName_DoesNotThrowOnValidName(string functionName)
        {
            try
            {
                ScriptHost.ValidateName(functionName);
            }
            catch (InvalidOperationException)
            {
                Assert.True(false, $"Valid function name {functionName} failed validation.");
            }
        }

#if WEBROUTING
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

            // verify that empty route is defaulted to function name
            function = new Mock<FunctionDescriptor>(MockBehavior.Strict, "test6", null, metadata, null, null, null, null);
            attribute = new HttpTriggerAttribute();
            function.Setup(p => p.GetTriggerAttributeOrNull<HttpTriggerAttribute>()).Returns(() => attribute);
            ScriptHost.ValidateFunction(function.Object, httpFunctions);
            Assert.Equal(4, httpFunctions.Count);
            Assert.True(httpFunctions.ContainsKey("test6"));
            Assert.Equal("test6", attribute.Route);
        }
#endif

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
        public void IsFunction_ReturnsExpectedResult()
        {
            Mock<IScriptHostEnvironment> mockEnvironment = new Mock<IScriptHostEnvironment>(MockBehavior.Strict);
            var config = new ScriptHostConfiguration();
            var eventManager = new Mock<IScriptEventManager>();
            var mockHost = new Mock<ScriptHost>(MockBehavior.Strict, new object[] { mockEnvironment.Object, eventManager.Object, config, null, null, null });

            var functions = new Collection<FunctionDescriptor>();
            var functionErrors = new Dictionary<string, Collection<string>>();
            mockHost.Setup(p => p.Functions).Returns(functions);
            mockHost.Setup(p => p.FunctionErrors).Returns(functionErrors);

            var parameters = new Collection<ParameterDescriptor>();
            parameters.Add(new ParameterDescriptor("param1", typeof(string)));
            var metadata = new FunctionMetadata();
            var invoker = new TestInvoker();
            var function = new FunctionDescriptor("TestFunction", invoker, metadata, parameters, null, null, null);
            functions.Add(function);

            var errors = new Collection<string>();
            errors.Add("A really really bad error!");
            functionErrors.Add("ErrorFunction", errors);

            var host = mockHost.Object;
            Assert.True(host.IsFunction("TestFunction"));
            Assert.True(host.IsFunction("ErrorFunction"));
            Assert.False(host.IsFunction("DoesNotExist"));
            Assert.False(host.IsFunction(string.Empty));
            Assert.False(host.IsFunction(null));
        }

        [Fact]
        public void Initialize_LogsWarningForExplicitlySetHostId()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerProviderFactory = new TestLoggerProviderFactory(loggerProvider, false);

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests_Initialize_LogsWarningForExplicitlySetHostId");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // Set id in the host.json
            string hostJsonContent = @"
            {
                'id': 'foobar'
            }";
            File.WriteAllText(Path.Combine(rootPath, "host.json"), hostJsonContent);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };

            // This id will be over written
            config.HostConfig.HostId = ID;
            var environment = new Mock<IScriptHostEnvironment>();
            var eventManager = new Mock<IScriptEventManager>();

            var host = new ScriptHost(environment.Object, eventManager.Object, config, null, loggerProviderFactory);
            host.Initialize();

            Assert.DoesNotMatch(ID, config.HostConfig.HostId);
            Assert.Matches("foobar", config.HostConfig.HostId);

            // We should have a warning for host id in the start up logger
            var logger = loggerProvider.CreatedLoggers.First(x => x.Category == "Host.Startup");
            Assert.Single(logger.GetLogMessages(), x => x.FormattedMessage.Contains("Host id explicitly set in the host.json."));
        }

        public class AssemblyMock : Assembly
        {
            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                return new Attribute[] { };
            }
        }

        public class TestFixture
        {
            public TestFixture()
            {
                ScriptHostConfiguration config = new ScriptHostConfiguration();
                config.HostConfig.HostId = ID;
                var environment = new Mock<IScriptHostEnvironment>();
                var eventManager = new Mock<IScriptEventManager>();

                Host = new ScriptHost(environment.Object, eventManager.Object, config);
                Host.Initialize();
            }

            public ScriptHost Host { get; private set; }
        }
    }
}