// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptHostTests : IClassFixture<ScriptHostTests.TestFixture>
    {
        private const string ID = "5a709861cab44e68bfed5d2c2fe7fc0c";
        private readonly TestFixture _fixture;
        private readonly ScriptSettingsManager _settingsManager;

        public ScriptHostTests(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
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
                RootScriptPath = Path.Combine(Environment.CurrentDirectory, @"..\..\..\..\sample")
            };
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var functionErrors = new Dictionary<string, Collection<string>>();
            var metadata = ScriptHost.ReadFunctionMetadata(config, traceWriter, functionErrors);
            Assert.Equal(49, metadata.Count);
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
            Assert.False(host.InDebugMode);

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
            Assert.Throws<ConfigurationErrorsException>(() => ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_NoFiles_ReturnsNull()
        {
            var functionConfig = new JObject();
            string[] functionFiles = new string[0];
            var fileSystem = new MockFileSystem();
            fileSystem.AddDirectory(@"c:\functions");
            Assert.Throws<ConfigurationErrorsException>(() => ScriptHost.DeterminePrimaryScriptFile(functionConfig, @"c:\functions", fileSystem));
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

            var ex = Assert.Throws<FormatException>(() =>
            {
                ScriptHost.Create(environment.Object, scriptConfig, _settingsManager);
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
        public void Create_InvalidFunctionNames_DoesNotCreateFunctionAndLogsFailure(string functionName)
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
                var scriptHost = ScriptHost.Create(environment.Object, scriptConfig, _settingsManager);

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
            TraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(scriptConfig.HostConfig, config, new TestTraceWriter(TraceLevel.Verbose));
            provider.Initialize();

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

            provider = new WebJobsCoreScriptBindingProvider(scriptConfig.HostConfig, config, new TestTraceWriter(TraceLevel.Verbose));
            provider.Initialize();

            Assert.Equal(5000, scriptConfig.HostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(17, scriptConfig.HostConfig.Queues.BatchSize);
            Assert.Equal(3, scriptConfig.HostConfig.Queues.MaxDequeueCount);
            Assert.Equal(123, scriptConfig.HostConfig.Queues.NewBatchThreshold);
            Assert.Equal(TimeSpan.FromSeconds(30), scriptConfig.HostConfig.Queues.VisibilityTimeout);
        }

        [Fact]
        public void ApplyConfiguration_Blobs()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject blobsConfig = new JObject();
            config["blobs"] = blobsConfig;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();
            TraceWriter traceWriter = new TestTraceWriter(TraceLevel.Verbose);

            WebJobsCoreScriptBindingProvider provider = new WebJobsCoreScriptBindingProvider(scriptConfig.HostConfig, config, new TestTraceWriter(TraceLevel.Verbose));
            provider.Initialize();

            Assert.True(scriptConfig.HostConfig.Blobs.CentralizedPoisonQueue);

            blobsConfig["centralizedPoisonQueue"] = false;

            provider = new WebJobsCoreScriptBindingProvider(scriptConfig.HostConfig, config, new TestTraceWriter(TraceLevel.Verbose));
            provider.Initialize();

            Assert.False(scriptConfig.HostConfig.Blobs.CentralizedPoisonQueue);
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
        public void ApplyConfiguration_Http()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject http = new JObject();
            config["http"] = http;
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.Equal(ScriptConstants.DefaultHttpRoutePrefix, scriptConfig.HttpRoutePrefix);

            http["routePrefix"] = "myprefix";

            ScriptHost.ApplyConfiguration(config, scriptConfig);

            Assert.Equal("myprefix", scriptConfig.HttpRoutePrefix);
        }

        // with swagger with setting name with value
        // with swagger with setting name with wrong value set
        [Fact]
        public void ApplyConfiguration_Swagger()
        {
            JObject config = new JObject();
            config["id"] = ID;
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            // no swagger section
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(false, scriptConfig.SwaggerEnabled);

            // empty swagger section
            JObject swagger = new JObject();
            config["swagger"] = swagger;
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(false, scriptConfig.SwaggerEnabled);

            // swagger section present, with swagger mode set to null
            swagger["enabled"] = string.Empty;
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(false, scriptConfig.SwaggerEnabled);

            // swagger section present, with swagger mode set to true
            swagger["enabled"] = true;
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(true, scriptConfig.SwaggerEnabled);

            // swagger section present, with swagger mode set to invalid
            swagger["enabled"] = "invalid";
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(false, scriptConfig.SwaggerEnabled);
        }

        [Fact]
        public void ApplyConfiguration_Tracing()
        {
            JObject config = new JObject();
            config["id"] = ID;
            JObject tracing = new JObject();
            config["tracing"] = tracing;
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            Assert.Equal(TraceLevel.Info, scriptConfig.HostConfig.Tracing.ConsoleLevel);
            Assert.Equal(FileLoggingMode.Never, scriptConfig.FileLoggingMode);

            tracing["consoleLevel"] = "Verbose";
            tracing["fileLoggingMode"] = "Always";

            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(TraceLevel.Verbose, scriptConfig.HostConfig.Tracing.ConsoleLevel);
            Assert.Equal(FileLoggingMode.Always, scriptConfig.FileLoggingMode);

            tracing["fileLoggingMode"] = "DebugOnly";
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(FileLoggingMode.DebugOnly, scriptConfig.FileLoggingMode);
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
        public void ApplyConfiguration_TimeoutDefaults5Minutes_IfDynamic()
        {
            JObject config = new JObject();
            config["id"] = ID;

            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration();

            try
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");
                ScriptHost.ApplyConfiguration(config, scriptConfig);
                Assert.Equal(TimeSpan.FromMinutes(5), scriptConfig.FunctionTimeout);
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

            config["functionTimeout"] = "00:05:01";
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(TimeSpan.FromSeconds(301), scriptConfig.FunctionTimeout);

            config["functionTimeout"] = "00:00:00.9";
            ScriptHost.ApplyConfiguration(config, scriptConfig);
            Assert.Equal(TimeSpan.FromMilliseconds(900), scriptConfig.FunctionTimeout);
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

                config["functionTimeout"] = "00:05:01";
                Assert.Throws<ArgumentException>(() => ScriptHost.ApplyConfiguration(config, scriptConfig));

                config["functionTimeout"] = "00:00:00.9";
                Assert.Throws<ArgumentException>(() => ScriptHost.ApplyConfiguration(config, scriptConfig));
            }
            finally
            {
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, null);
            }
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
            FunctionDescriptor function = new FunctionDescriptor("TimerFunction", new TestInvoker(), metadata, new Collection<ParameterDescriptor>());
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
            function = new FunctionDescriptor("TimerFunction", new TestInvoker(), metadata, new Collection<ParameterDescriptor>());
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
                ScriptHost.ValidateFunctionName(functionName);
            });

            Assert.Equal(string.Format("'{0}' is not a valid function name.", functionName), ex.Message);
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
                ScriptHost.ValidateFunctionName(functionName);
            }
            catch (InvalidOperationException)
            {
                Assert.True(false, $"Valid function name {functionName} failed validation.");
            }
        }

        [Fact]
        public void HttpRoutesConflict_ReturnsExpectedResult()
        {
            var first = new HttpTriggerBindingMetadata
            {
                Route = "foo/bar/baz"
            };
            var second = new HttpTriggerBindingMetadata
            {
                Route = "foo/bar"
            };
            Assert.False(ScriptHost.HttpRoutesConflict(first, second));
            Assert.False(ScriptHost.HttpRoutesConflict(second, first));

            first = new HttpTriggerBindingMetadata
            {
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerBindingMetadata
            {
                Route = "foo/bar/baz"
            };
            Assert.True(ScriptHost.HttpRoutesConflict(first, second));
            Assert.True(ScriptHost.HttpRoutesConflict(second, first));

            // no conflict since methods do not intersect
            first = new HttpTriggerBindingMetadata
            {
                Methods = new Collection<HttpMethod>() { HttpMethod.Get, HttpMethod.Head },
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerBindingMetadata
            {
                Methods = new Collection<HttpMethod>() { HttpMethod.Post, HttpMethod.Put },
                Route = "foo/bar/baz"
            };
            Assert.False(ScriptHost.HttpRoutesConflict(first, second));
            Assert.False(ScriptHost.HttpRoutesConflict(second, first));

            first = new HttpTriggerBindingMetadata
            {
                Methods = new Collection<HttpMethod>() { HttpMethod.Get, HttpMethod.Head },
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerBindingMetadata
            {
                Route = "foo/bar/baz"
            };
            Assert.True(ScriptHost.HttpRoutesConflict(first, second));
            Assert.True(ScriptHost.HttpRoutesConflict(second, first));

            first = new HttpTriggerBindingMetadata
            {
                Methods = new Collection<HttpMethod>() { HttpMethod.Get, HttpMethod.Head, HttpMethod.Put, HttpMethod.Post },
                Route = "foo/bar/baz"
            };
            second = new HttpTriggerBindingMetadata
            {
                Methods = new Collection<HttpMethod>() { HttpMethod.Put },
                Route = "foo/bar/baz"
            };
            Assert.True(ScriptHost.HttpRoutesConflict(first, second));
            Assert.True(ScriptHost.HttpRoutesConflict(second, first));
        }

        [Fact]
        public void TryParseFunctionMetadata_ValidatesHttpRoutes()
        {
            // first add an http function
            JObject functionConfig = new JObject();
            functionConfig.Add("bindings", new JArray(new JObject
            {
                { "type", "httpTrigger" },
                { "name", "req" },
                { "direction", "in" },
                { "methods", new JArray("get") },
                { "route", "products/{category}/{id?}" }
            }));
            var mappedHttpFunctions = new Dictionary<string, HttpTriggerBindingMetadata>();
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            FunctionMetadata functionMetadata = null;
            string functionError = null;
            var fileSystem = new MockFileSystem();
            fileSystem.AddFile(@"c:\functions\test\run.csx", new MockFileData(string.Empty));
            fileSystem.AddFile(@"c:\functions\test2\run.csx", new MockFileData(string.Empty));
            fileSystem.AddFile(@"c:\functions\test3\run.csx", new MockFileData(string.Empty));
            fileSystem.AddFile(@"c:\functions\test4\run.csx", new MockFileData(string.Empty));
            fileSystem.AddFile(@"c:\functions\test5\run.csx", new MockFileData(string.Empty));
            bool result = ScriptHost.TryParseFunctionMetadata("test", functionConfig, mappedHttpFunctions, traceWriter, @"c:\functions\test", ScriptSettingsManager.Instance, out functionMetadata, out functionError, fileSystem);
            Assert.True(result);
            Assert.NotNull(functionMetadata);
            Assert.Null(functionError);
            Assert.Equal(1, mappedHttpFunctions.Count);
            Assert.True(mappedHttpFunctions.ContainsKey("test"));
            Assert.Equal(@"c:\functions\test\run.csx", functionMetadata.ScriptFile);

            // add another for a completely different route
            functionConfig["bindings"] = new JArray(new JObject
            {
                { "type", "httpTrigger" },
                { "name", "req" },
                { "direction", "in" },
                { "methods", new JArray("get") },
                { "route", "/foo/bar/baz/" }
            });
            functionMetadata = null;
            functionError = null;
            result = ScriptHost.TryParseFunctionMetadata("test2", functionConfig, mappedHttpFunctions, traceWriter, @"c:\functions\test2", ScriptSettingsManager.Instance, out functionMetadata, out functionError, fileSystem);
            Assert.True(result);
            Assert.NotNull(functionMetadata);
            Assert.Null(functionError);
            Assert.True(mappedHttpFunctions.ContainsKey("test2"));
            Assert.Equal(2, mappedHttpFunctions.Count);

            // add another that varies from another only by http method
            functionConfig["bindings"] = new JArray(new JObject
            {
                { "type", "httpTrigger" },
                { "name", "req" },
                { "direction", "in" },
                { "methods", new JArray("put", "post") },
                { "route", "/foo/bar/baz" }
            });
            functionMetadata = null;
            functionError = null;
            result = ScriptHost.TryParseFunctionMetadata("test3", functionConfig, mappedHttpFunctions, traceWriter, @"c:\functions\test3", ScriptSettingsManager.Instance, out functionMetadata, out functionError, fileSystem);
            Assert.True(result);
            Assert.NotNull(functionMetadata);
            Assert.Null(functionError);
            Assert.True(mappedHttpFunctions.ContainsKey("test3"));
            Assert.Equal(3, mappedHttpFunctions.Count);

            // now try to add a function for the same route
            // where the http methods overlap
            functionConfig["bindings"] = new JArray(new JObject
            {
                { "type", "httpTrigger" },
                { "name", "req" },
                { "direction", "in" },
                { "route", "foo/bar/baz" }
            });
            functionMetadata = null;
            functionError = null;
            result = ScriptHost.TryParseFunctionMetadata("test4", functionConfig, mappedHttpFunctions, traceWriter, @"c:\functions\test4", ScriptSettingsManager.Instance, out functionMetadata, out functionError, fileSystem);
            Assert.False(result);
            Assert.NotNull(functionMetadata);
            Assert.True(functionError.StartsWith("The route specified conflicts with the route defined by function"));
            Assert.Equal(3, mappedHttpFunctions.Count);

            // try to add a route under reserved admin route
            functionConfig["bindings"] = new JArray(new JObject
            {
                { "type", "httpTrigger" },
                { "name", "req" },
                { "direction", "in" },
                { "route", "admin/foo/bar" }
            });
            functionMetadata = null;
            functionError = null;
            result = ScriptHost.TryParseFunctionMetadata("test5", functionConfig, mappedHttpFunctions, traceWriter, @"c:\functions\test5", ScriptSettingsManager.Instance, out functionMetadata, out functionError, fileSystem);
            Assert.False(result);
            Assert.NotNull(functionMetadata);
            Assert.Equal(3, mappedHttpFunctions.Count);
            Assert.Equal("The specified route conflicts with one or more built in routes.", functionError);
        }

        [Fact]
        public void IsFunction_ReturnsExpectedResult()
        {
            Mock<IScriptHostEnvironment> mockEnvironment = new Mock<IScriptHostEnvironment>(MockBehavior.Strict);
            var config = new ScriptHostConfiguration();

            var mockHost = new Mock<ScriptHost>(MockBehavior.Strict, new object[] { mockEnvironment.Object, config, null });

            var functions = new Collection<FunctionDescriptor>();
            var functionErrors = new Dictionary<string, Collection<string>>();
            mockHost.Setup(p => p.Functions).Returns(functions);
            mockHost.Setup(p => p.FunctionErrors).Returns(functionErrors);

            var parameters = new Collection<ParameterDescriptor>();
            parameters.Add(new ParameterDescriptor("param1", typeof(string)));
            var metadata = new FunctionMetadata();
            var invoker = new TestInvoker();
            var function = new FunctionDescriptor("TestFunction", invoker, metadata, parameters);
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
                Host = ScriptHost.Create(environment.Object, config);
            }

            public ScriptHost Host { get; private set; }
        }
    }
}
