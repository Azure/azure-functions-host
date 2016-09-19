// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
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

        public ScriptHostTests(TestFixture fixture)
        {
            _fixture = fixture;
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
        public async Task OnDebugModeFileChanged_TriggeredWhenDebugFileUpdated()
        {
            ScriptHost host = _fixture.Host;
            string debugSentinelFilePath = Path.Combine(host.ScriptConfig.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);

            host.LastDebugNotify = DateTime.MinValue;
            Assert.False(host.InDebugMode);

            // verify that our file watcher for the debug file is configured
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

            host.LastDebugNotify = DateTime.Now - TimeSpan.FromSeconds(60 * ScriptHost.DebugModeTimeoutMinutes);
            Assert.False(host.InDebugMode);

            host.LastDebugNotify = DateTime.Now - TimeSpan.FromSeconds(60 * (ScriptHost.DebugModeTimeoutMinutes - 1));
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
            string[] functionFiles = new string[]
            {
                @"c:\functions\queueTrigger.py",
                @"c:\functions\helper.py",
                @"c:\functions\test.txt"
            };
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles);
            Assert.Equal(@"c:\functions\queueTrigger.py", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_ConfigTrumpsConvention()
        {
            JObject functionConfig = new JObject()
            {
                { "scriptFile", "queueTrigger.py" }
            };
            string[] functionFiles = new string[]
            {
                @"c:\functions\run.py",
                @"c:\functions\queueTrigger.py",
                @"c:\functions\helper.py",
                @"c:\functions\test.txt"
            };
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles);
            Assert.Equal(@"c:\functions\queueTrigger.py", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_NoClearPrimary_ReturnsNull()
        {
            JObject functionConfig = new JObject();
            string[] functionFiles = new string[]
            {
                @"c:\functions\foo.py",
                @"c:\functions\queueTrigger.py",
                @"c:\functions\helper.py",
                @"c:\functions\test.txt"
            };
            Assert.Null(ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_NoFiles_ReturnsNull()
        {
            JObject functionConfig = new JObject();
            string[] functionFiles = new string[0];
            Assert.Null(ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles));
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunFilePresent()
        {
            JObject functionConfig = new JObject();
            string[] functionFiles = new string[]
            {
                @"c:\functions\Run.csx",
                @"c:\functions\Helper.csx",
                @"c:\functions\test.txt"
            };
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_SingleFile()
        {
            JObject functionConfig = new JObject();
            string[] functionFiles = new string[]
            {
                @"c:\functions\Run.csx"
            };
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles);
            Assert.Equal(@"c:\functions\Run.csx", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_RunTrumpsIndex()
        {
            JObject functionConfig = new JObject();
            string[] functionFiles = new string[]
            {
                @"c:\functions\run.js",
                @"c:\functions\index.js",
                @"c:\functions\test.txt"
            };
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles);
            Assert.Equal(@"c:\functions\run.js", scriptFile);
        }

        [Fact]
        public void DeterminePrimaryScriptFile_MultipleFiles_IndexFilePresent()
        {
            JObject functionConfig = new JObject();
            string[] functionFiles = new string[]
            {
                @"c:\functions\index.js",
                @"c:\functions\test.txt"
            };
            string scriptFile = ScriptHost.DeterminePrimaryScriptFile(functionConfig, functionFiles);
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

            var ex = Assert.Throws<FormatException>(() =>
            {
                ScriptHost.Create(scriptConfig);
            });

            Assert.Equal(string.Format("Unable to parse {0} file.", ScriptConstants.HostMetadataFileName), ex.Message);
            Assert.Equal("Invalid property identifier character: ~. Path '', line 2, position 4.", ex.InnerException.Message);
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

            queuesConfig["maxPollingInterval"] = 5000;
            queuesConfig["batchSize"] = 17;
            queuesConfig["maxDequeueCount"] = 3;
            queuesConfig["newBatchThreshold"] = 123;

            provider = new WebJobsCoreScriptBindingProvider(scriptConfig.HostConfig, config, new TestTraceWriter(TraceLevel.Verbose));
            provider.Initialize();

            Assert.Equal(5000, scriptConfig.HostConfig.Queues.MaxPollingInterval.TotalMilliseconds);
            Assert.Equal(17, scriptConfig.HostConfig.Queues.BatchSize);
            Assert.Equal(3, scriptConfig.HostConfig.Queues.MaxDequeueCount);
            Assert.Equal(123, scriptConfig.HostConfig.Queues.NewBatchThreshold);
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
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");
                ScriptHost.ApplyConfiguration(config, scriptConfig);
                Assert.Equal(TimeSpan.FromMinutes(5), scriptConfig.FunctionTimeout);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, null);
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
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

                config["functionTimeout"] = "00:05:01";
                Assert.Throws<ArgumentException>(() => ScriptHost.ApplyConfiguration(config, scriptConfig));

                config["functionTimeout"] = "00:00:00.9";
                Assert.Throws<ArgumentException>(() => ScriptHost.ApplyConfiguration(config, scriptConfig));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, null);
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
        [InlineData("host")]
        [InlineData("Host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
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
                Host = ScriptHost.Create(config);
            }

            public ScriptHost Host { get; private set; }
        }
    }
}
