// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.WorkerConfigurationResolverTestsHelper;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationProviderBaseTests
    {
        public WorkerConfigurationProviderBaseTests()
        {
            EnvironmentExtensions.ClearCache();
        }

        [Fact]
        public void DefaultLanguageWorkersDir()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigurationProviderBase).Assembly.Location).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testEnvironment = new TestEnvironment();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var testLoggerFactory = GetTestLoggerFactory();

            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);

            Assert.Equal(expectedWorkersDir, optionsMonitor.CurrentValue.WorkersRootDirPath);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetDefaultWorkersDirectory_Returns_Expected(bool expectedValue)
        {
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(WorkerConfigurationProviderBase).Assembly.Location).LocalPath);
            string defaultWorkersDirPath = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var fileSystemMock = new Mock<IFileSystem>();
            var expectedWorkersDirIsCurrentDir = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var expectedWorkersDirIsParentDir = Path.Combine(Directory.GetParent(assemblyLocalPath).FullName, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var loggerFactory = GetTestLoggerFactory();
            var trimmedAssemblyDir = assemblyLocalPath.TrimEnd(Path.DirectorySeparatorChar);

            var parentDirInfoMock = new Mock<DirectoryInfoBase>();
            parentDirInfoMock.Setup(d => d.FullName).Returns(Directory.GetParent(assemblyLocalPath).FullName);

            var parentDirInfoMock2 = new Mock<DirectoryInfoBase>();
            parentDirInfoMock2.Setup(d => d.FullName).Returns(Directory.GetParent(trimmedAssemblyDir).FullName);

            fileSystemMock.Setup(f => f.Directory.Exists(defaultWorkersDirPath)).Returns(expectedValue);
            fileSystemMock.Setup(f => f.Directory.GetParent(trimmedAssemblyDir)).Returns(parentDirInfoMock2.Object);
            fileSystemMock.Setup(f => f.Directory.GetParent(defaultWorkersDirPath)).Returns(parentDirInfoMock.Object);
            fileSystemMock.Setup(f => f.Path.Combine(It.IsAny<string>(), RpcWorkerConstants.DefaultWorkersDirectoryName))
                .Returns((string dir, string workersDirName) => Path.Combine(dir, workersDirName));

            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var env = new Mock<IEnvironment>();
            var optionsSetup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, config, env.Object, fileSystemMock.Object, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions()));

            if (expectedValue)
            {
                Assert.Equal(expectedWorkersDirIsCurrentDir, optionsSetup.GetDefaultWorkersDirectory());
            }
            else
            {
                Assert.Equal(expectedWorkersDirIsParentDir, optionsSetup.GetDefaultWorkersDirectory());
            }
        }

        [Fact]
        public void LanguageWorker_WorkersDir_Set()
        {
            var expectedWorkersDir = @"d:\testWorkersDir";
            var config = new ConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = expectedWorkersDir
                   })
                   .Build();
            var testLogger = new TestLogger("test");
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var mockLogger = new Mock<ILoggerFactory>();
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testEnvironment = new TestEnvironment();

            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);

            Assert.Equal(expectedWorkersDir, optionsMonitor.CurrentValue.WorkersRootDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_NotSet()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigurationProviderBase).Assembly.Location).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       ["languageWorker"] = "test"
                   });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var testLoggerFactory = GetTestLoggerFactory();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var testEnvironment = new TestEnvironment();

            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);

            Assert.Equal(expectedWorkersDir, optionsMonitor.CurrentValue.WorkersRootDirPath);
        }

        [Fact]
        public void WorkerDescription_Skipped_When_Profile_Disables_Worker()
        {
            // The "TestWorkers" directory has 2 workers: "worker1" and "worker2".
            // "worker2" has 2 profiles. The first profile will be skipped since the condition is not met.
            // The second profile will be applied since the condition is met. The second profile updates
            // the "IsDisabled" property to True and will cause the workerDescription to be skipped.

            var testPath = Path.GetDirectoryName(new Uri(typeof(WorkerConfigurationProviderBaseTests).Assembly.Location).LocalPath);
            var testWorkersDirectory = Path.Combine(testPath, "TestWorkers");
            var testEnvVariables = new Dictionary<string, string>
            {
                { $"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}", testWorkersDirectory }
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                .AddInMemoryCollection(testEnvVariables);
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var testLoggerFactory = GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable("ENV_VAR_BAR", "True");
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();

            var loggerFactory = GetTestLoggerFactory();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object);
            var dynamicProviderLogger = new TestLogger<DynamicWorkerConfigurationProvider>();
            var providers = GetProviders(testLoggerFactory, dynamicProviderLogger, testMetricLogger, FileUtility.Instance, testProfileManager.Object, SystemRuntimeInformation.Instance, optionsMonitor);

            var workerConfigurationResolver = new WorkerConfigurationResolver(providers);
            var workerConfigs = workerConfigurationResolver.GetWorkerConfigs().Values.ToList();
            var errors = testLogger.GetLogMessages().Where(m => m.Exception != null).ToList();
            Assert.False(testLogger.GetLogMessages().Any(m => m.Exception != null), "There should not be an exception logged while executing GetConfigs method.");
            Assert.Equal(1, workerConfigs.Count);
            Assert.EndsWith("worker1\\1.bat", workerConfigs[0].Description.DefaultWorkerPath);
        }

        [Fact]
        public void JavaPath_FromEnvVars()
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (string.IsNullOrWhiteSpace(javaHome))
            {
                // if the var doesn't exist, set something temporary to make it at least work
                Environment.SetEnvironmentVariable("JAVA_HOME", Path.GetTempPath());
            }
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder();
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();

            var loggerFactory = GetTestLoggerFactory();
            var testLogger = loggerFactory.CreateLogger("test");
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var testEnvironment = new TestEnvironment();
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object);
            var dynamicProviderLogger = new TestLogger<DynamicWorkerConfigurationProvider>();
            var providers = GetProviders(loggerFactory, dynamicProviderLogger, testMetricLogger, FileUtility.Instance, testProfileManager.Object, SystemRuntimeInformation.Instance, optionsMonitor);

            var workerConfigurationResolver = new WorkerConfigurationResolver(providers);
            var workerConfigs = workerConfigurationResolver.GetWorkerConfigs().Values.ToList();
            var javaPath = workerConfigs.FirstOrDefault(c => c.Description.Language.Equals("java", StringComparison.OrdinalIgnoreCase)).Description.DefaultExecutablePath;
            Assert.DoesNotContain(@"%JAVA_HOME%", javaPath);
            Assert.Contains(@"/bin/java", javaPath);
        }

        [Theory]
        [InlineData("3.9", "3.9")]
        [InlineData(null, "3.12")]
        public void DefaultWorkerConfigs_Overrides_DefaultWorkerRuntimeVersion_AppSetting(string setting, string output)
        {
            var testEnvVariables = new Dictionary<string, string>
            {
                { "languageWorkers:python:defaultRuntimeVersion", setting }
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                .AddInMemoryCollection(testEnvVariables);
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);

            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Windows");

            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            var workerProfileManager = new WorkerProfileManager(workerProfileLogger, testEnvironment);

            using var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables);

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var testLogger = loggerFactory.CreateLogger("test");
            var testMetricLogger = new TestMetricsLogger();

            var testScriptHostManager = new Mock<IScriptHostManager>();
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);
            var dynamicProviderLogger = new TestLogger<DynamicWorkerConfigurationProvider>();

            var providers = GetProviders(loggerFactory, dynamicProviderLogger, testMetricLogger, FileUtility.Instance, workerProfileManager, SystemRuntimeInformation.Instance, optionsMonitor);

            var workerConfigurationResolver = new WorkerConfigurationResolver(providers);

            var workerConfigs = workerConfigurationResolver.GetWorkerConfigs().Values.ToList();
            var pythonWorkerConfig = workerConfigs.FirstOrDefault(w => w.Description.Language.Equals("python", StringComparison.OrdinalIgnoreCase));
            var powershellWorkerConfig = workerConfigs.FirstOrDefault(w => w.Description.Language.Equals("powershell", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(5, workerConfigs.Count);
            Assert.NotNull(pythonWorkerConfig);
            Assert.NotNull(powershellWorkerConfig);
            Assert.Equal(output, pythonWorkerConfig.Description.DefaultRuntimeVersion);
            Assert.Equal("7.4", powershellWorkerConfig.Description.DefaultRuntimeVersion);
        }

        [Theory]
        [InlineData("7.4", "7.4")]
        [InlineData("7.2", "7.2")]
        [InlineData(null, "7.4")]
        public void DefaultWorkerConfigs_Overrides_VersionAppSetting(string setting, string output)
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME_VERSION", setting);
            testEnvironment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "powerShell");
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder();
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();

            var loggerFactory = GetTestLoggerFactory();
            var testLogger = loggerFactory.CreateLogger("test");
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);
            var dynamicProviderLogger = new TestLogger<DynamicWorkerConfigurationProvider>();

            var providers = GetProviders(loggerFactory, dynamicProviderLogger, testMetricLogger, FileUtility.Instance, testProfileManager.Object, SystemRuntimeInformation.Instance, optionsMonitor);

            var resolver = new WorkerConfigurationResolver(providers);
            var workerConfigs = resolver.GetWorkerConfigs().Values.ToList();
            var powershellWorkerConfig = workerConfigs.FirstOrDefault(w => w.Description.Language.Equals("powershell", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, workerConfigs.Count);
            Assert.NotNull(powershellWorkerConfig);
            Assert.Equal(output, powershellWorkerConfig.Description.DefaultRuntimeVersion);
        }

        [Theory]
        [InlineData("python", "Python", false, true)]
        [InlineData("python", "NOde", false, false)]
        [InlineData("python", "", false, true)]
        [InlineData("python", null, false, true)]
        [InlineData("python", "NOde", true, true)]
        [InlineData("python", null, true, true)]
        public void ShouldAddProvider_Returns_Expected(string workerLanguage, string workerRuntime, bool placeholderMode, bool expectedResult)
        {
            var testEnvironment = new TestEnvironment();

            if (placeholderMode)
            {
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }
            if (!string.IsNullOrEmpty(workerRuntime))
            {
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
            }
            var loggerFactory = GetTestLoggerFactory();
            var testLogger = loggerFactory.CreateLogger("test");

            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
            Assert.Equal(expectedResult, WorkerConfigurationProviderBase.ShouldAddWorkerConfig(workerLanguage, placeholderMode, false, testLogger, workerRuntime));
        }

        [Theory]
        [InlineData(true, true, false, 4, 50, "00:00:15")]
        [InlineData(false, false, false, 4, 15, "00:00:30")]
        [InlineData(false, true, false, 4, 15, "00:00:30")]
        [InlineData(false, true, true, 4, 8, "00:00:05")]
        public void GetWorkerProcessCount_Tests(bool defaultWorkerConfig, bool setProcessCountToNumberOfCpuCores, bool setWorkerCountInEnv, int minProcessCount, int maxProcessCount, string processStartupInterval)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();

                if (!defaultWorkerConfig)
                {
                    writer.WritePropertyName(WorkerConstants.ProcessCount);
                    writer.WriteStartObject();

                    writer.WriteNumber("processcount", minProcessCount);
                    writer.WriteNumber("MaxProcessCount", maxProcessCount);
                    writer.WriteString("processStartupInterval", processStartupInterval);
                    writer.WriteBoolean("SETPROCESSCOUNTTONUMBEROFCPUCORES", setProcessCountToNumberOfCpuCores);

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            using var jsonDocument = JsonDocument.Parse(stream.ToArray());
            JsonElement workerConfig = jsonDocument.RootElement;
            var testEnvironment = new TestEnvironment();

            if (setWorkerCountInEnv)
            {
                testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName, "7");
            }

            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");

            var testScriptHostManager = new Mock<IScriptHostManager>();
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);
            var mockLogger = new Mock<ILoggerFactory>();
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var dynamicProviderLogger = new TestLogger<DynamicWorkerConfigurationProvider>();

            var providers = GetProviders(mockLogger.Object, dynamicProviderLogger, testMetricLogger, FileUtility.Instance, testProfileManager.Object, SystemRuntimeInformation.Instance, optionsMonitor);

            var workerConfigurationResolver = new WorkerConfigurationResolver(providers);

            var result = WorkerConfigurationProviderBase.GetWorkerProcessCount(workerConfig, testEnvironment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName), testEnvironment.GetEffectiveCoresCount());

            if (defaultWorkerConfig)
            {
                // Verify defaults
                Assert.Equal(10, result.MaxProcessCount);
                Assert.Equal(1, result.ProcessCount);
                Assert.Equal(TimeSpan.FromSeconds(10), result.ProcessStartupInterval);
                Assert.False(result.SetProcessCountToNumberOfCpuCores);
                return;
            }

            if (setWorkerCountInEnv)
            {
                Assert.Equal(7, result.ProcessCount);
            }
            else
            {
                if (setWorkerCountInEnv && setProcessCountToNumberOfCpuCores)
                {
                    Assert.Equal(7, result.ProcessCount);
                }
                else if (setProcessCountToNumberOfCpuCores)
                {
                    Assert.Equal(testEnvironment.GetEffectiveCoresCount(), result.ProcessCount);
                }
            }

            Assert.Equal(TimeSpan.Parse(processStartupInterval), result.ProcessStartupInterval);
            Assert.Equal(maxProcessCount, result.MaxProcessCount);
        }

        [Fact]
        public void GetWorkerProcessCount_ThrowsException_Tests()
        {
            JsonElement workerConfig = CreateWorkerConfig(-4, 10, "00:10:00", false);

            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");

            var testEnvironment = new TestEnvironment();
            var testScriptHostManager = new Mock<IScriptHostManager>();
            var optionsMonitor = GetTestWorkerConfigurationResolverOptions(config, testEnvironment, testScriptHostManager.Object, null);
            var mockLogger = new Mock<ILoggerFactory>();
            var testMetricLogger = new TestMetricsLogger();
            var testProfileManager = new Mock<IWorkerProfileManager>();
            var dynamicProviderLogger = new TestLogger<DynamicWorkerConfigurationProvider>();

            var providers = GetProviders(mockLogger.Object, dynamicProviderLogger, testMetricLogger, FileUtility.Instance, testProfileManager.Object, SystemRuntimeInformation.Instance, optionsMonitor);

            var workerConfigurationResolver = new WorkerConfigurationResolver(providers);

            var resultEx1 = Assert.Throws<ArgumentOutOfRangeException>(() => WorkerConfigurationProviderBase.GetWorkerProcessCount(workerConfig, testEnvironment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName), testEnvironment.GetEffectiveCoresCount()));
            Assert.Contains("ProcessCount must be greater than 0", resultEx1.Message);

            workerConfig = CreateWorkerConfig(40, 10, "00:10:00", false);
            var resultEx2 = Assert.Throws<ArgumentException>(() => WorkerConfigurationProviderBase.GetWorkerProcessCount(workerConfig, testEnvironment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName), testEnvironment.GetEffectiveCoresCount()));
            Assert.Contains("ProcessCount must not be greater than MaxProcessCount", resultEx2.Message);

            workerConfig = CreateWorkerConfig(10, 10, "-800", false);
            var resultEx3 = Assert.Throws<JsonException>(() => WorkerConfigurationProviderBase.GetWorkerProcessCount(workerConfig, testEnvironment.GetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName), testEnvironment.GetEffectiveCoresCount()));
            Assert.Contains("value could not be converted to System.TimeSpan", resultEx3.Message);
        }

        private static JsonElement CreateWorkerConfig(int processCount, int maxProcessCount, string processStartupInterval, bool setProcessCountToCores)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(WorkerConstants.ProcessCount);

                writer.WriteStartObject();
                writer.WriteNumber("ProcessCount", processCount);
                writer.WriteNumber("MaxProcessCount", maxProcessCount);
                writer.WriteString("ProcessStartupInterval", processStartupInterval);
                writer.WriteBoolean("SetProcessCountToNumberOfCpuCores", setProcessCountToCores);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            using var doc = JsonDocument.Parse(stream.ToArray());
            return doc.RootElement.Clone();
        }
    }
}