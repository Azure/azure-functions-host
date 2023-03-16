// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerConfigFactoryTests : IDisposable
    {
        private IWorkerProfileManager _testWorkerProfileManager;
        private TestSystemRuntimeInformation _testSysRuntimeInfo = new TestSystemRuntimeInformation();
        private TestEnvironment _testEnvironment;

        public RpcWorkerConfigFactoryTests()
        {
            _testEnvironment = new TestEnvironment();
            var workerProfileLogger = new TestLogger<WorkerProfileManager>();
            _testWorkerProfileManager = new WorkerProfileManager(workerProfileLogger, _testEnvironment);
        }

        public void Dispose()
        {
            _testEnvironment.Clear();
        }

        [Fact]
        public void DefaultLanguageWorkersDir()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.Location).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void GetDefaultWorkersDirectory_Returns_Expected()
        {
            string assemblyLocalPath = Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.Location).LocalPath);
            string defaultWorkersDirPath = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            Func<string, bool> testDirectoryExists = path =>
            {
                return false;
            };
            var expectedWorkersDirIsCurrentDir = Path.Combine(assemblyLocalPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var expectedWorkersDirIsParentDir = Path.Combine(Directory.GetParent(assemblyLocalPath).FullName, RpcWorkerConstants.DefaultWorkersDirectoryName);
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");

            Assert.Equal(expectedWorkersDirIsCurrentDir, RpcWorkerConfigFactory.GetDefaultWorkersDirectory(Directory.Exists));
            Assert.Equal(expectedWorkersDirIsParentDir, RpcWorkerConfigFactory.GetDefaultWorkersDirectory(testDirectoryExists));
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
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
        }

        [Fact]
        public void LanguageWorker_WorkersDir_NotSet()
        {
            var expectedWorkersDir = Path.Combine(Path.GetDirectoryName(new Uri(typeof(RpcWorkerConfigFactory).Assembly.Location).LocalPath), RpcWorkerConstants.DefaultWorkersDirectoryName);
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                   .AddInMemoryCollection(new Dictionary<string, string>
                   {
                       ["languageWorker"] = "test"
                   });
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            Assert.Equal(expectedWorkersDir, configFactory.WorkersDirPath);
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
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            var workerConfigs = configFactory.GetConfigs();
            var javaPath = workerConfigs.FirstOrDefault(c => c.Description.Language.Equals("java", StringComparison.OrdinalIgnoreCase)).Description.DefaultExecutablePath;
            Assert.DoesNotContain(@"%JAVA_HOME%", javaPath);
            Assert.Contains(@"/bin/java", javaPath);
        }

        [Fact]
        public void DefaultWorkerConfigs_Overrides_DefaultWorkerRuntimeVersion_AppSetting()
        {
            var testEnvVariables = new Dictionary<string, string>
            {
                { "languageWorkers:python:defaultRuntimeVersion", "3.8" }
            };
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                .AddInMemoryCollection(testEnvVariables);
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
            {
                var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
                var workerConfigs = configFactory.GetConfigs();
                var pythonWorkerConfig = workerConfigs.FirstOrDefault(w => w.Description.Language.Equals("python", StringComparison.OrdinalIgnoreCase));
                var powershellWorkerConfig = workerConfigs.FirstOrDefault(w => w.Description.Language.Equals("powershell", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(5, workerConfigs.Count);
                Assert.NotNull(pythonWorkerConfig);
                Assert.NotNull(powershellWorkerConfig);
                Assert.Equal("3.8", pythonWorkerConfig.Description.DefaultRuntimeVersion);
                Assert.Equal("7.2", powershellWorkerConfig.Description.DefaultRuntimeVersion);
            }
        }

        [Fact]
        public void DefaultWorkerConfigs_Overrides_VersionAppSetting()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME_VERSION", "7.2");
            testEnvironment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "powerShell");
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder();
            var config = configBuilder.Build();
            var scriptSettingsManager = new ScriptSettingsManager(config);
            var testLogger = new TestLogger("test");
            var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            var workerConfigs = configFactory.GetConfigs();
            var powershellWorkerConfig = workerConfigs.FirstOrDefault(w => w.Description.Language.Equals("powershell", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, workerConfigs.Count);
            Assert.NotNull(powershellWorkerConfig);
            Assert.Equal("7.2", powershellWorkerConfig.Description.DefaultRuntimeVersion);
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
            if (placeholderMode)
            {
                _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }
            if (!string.IsNullOrEmpty(workerRuntime))
            {
                _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            }
            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            RpcWorkerConfigFactory rpcWorkerConfigFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, workerRuntime);
            Assert.Equal(expectedResult, rpcWorkerConfigFactory.ShouldAddWorkerConfig(workerLanguage));
        }

        [Theory]
        [InlineData(true, true, false, 4, 50, "00:00:15")]
        [InlineData(false, false, false, 4, 15, "00:00:30")]
        [InlineData(false, true, false, 4, 15, "00:00:30")]
        [InlineData(false, true, true, 4, 8, "00:00:05")]
        public void GetWorkerProcessCount_Tests(bool defaultWorkerConfig, bool setProcessCountToNumberOfCpuCores, bool setWorkerCountInEnv, int minProcessCount, int maxProcessCount, string processStartupInterval)
        {
            JObject processCount = new JObject();
            processCount["ProcessCount"] = minProcessCount;
            processCount["MaxProcessCount"] = maxProcessCount;
            processCount["ProcessStartupInterval"] = processStartupInterval;
            processCount["SetProcessCountToNumberOfCpuCores"] = setProcessCountToNumberOfCpuCores;

            JObject workerConfig = new JObject();
            if (!defaultWorkerConfig)
            {
                workerConfig[WorkerConstants.ProcessCount] = processCount;
            }

            if (setWorkerCountInEnv)
            {
                _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName, "7");
            }

            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");

            RpcWorkerConfigFactory rpcWorkerConfigFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            var result = rpcWorkerConfigFactory.GetWorkerProcessCount(workerConfig);

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
                    Assert.Equal(_testEnvironment.GetEffectiveCoresCount(), result.ProcessCount);
                }
            }

            Assert.Equal(TimeSpan.Parse(processStartupInterval), result.ProcessStartupInterval);
            Assert.Equal(maxProcessCount, result.MaxProcessCount);
        }

        [Fact]
        public void GetWorkerProcessCount_ThrowsException_Tests()
        {
            JObject processCount = new JObject();
            processCount["ProcessCount"] = -4;
            processCount["MaxProcessCount"] = 10;
            processCount["ProcessStartupInterval"] = "00:10:00";
            processCount["SetProcessCountToNumberOfCpuCores"] = false;

            JObject workerConfig = new JObject();
            workerConfig[WorkerConstants.ProcessCount] = processCount;

            var config = new ConfigurationBuilder().Build();
            var testLogger = new TestLogger("test");
            RpcWorkerConfigFactory rpcWorkerConfigFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, new TestMetricsLogger(), _testWorkerProfileManager);
            var resultEx1 = Assert.Throws<ArgumentOutOfRangeException>(() => rpcWorkerConfigFactory.GetWorkerProcessCount(workerConfig));
            Assert.Contains("ProcessCount must be greater than 0", resultEx1.Message);

            processCount["ProcessCount"] = 40;
            var resultEx2 = Assert.Throws<ArgumentException>(() => rpcWorkerConfigFactory.GetWorkerProcessCount(workerConfig));
            Assert.Contains("ProcessCount must not be greater than MaxProcessCount", resultEx2.Message);

            processCount["ProcessStartupInterval"] = "-800";
            processCount["ProcessCount"] = 10;
            var resultEx3 = Assert.Throws<ArgumentOutOfRangeException>(() => rpcWorkerConfigFactory.GetWorkerProcessCount(workerConfig));
            Assert.Contains("The TimeSpan must not be negative", resultEx3.Message);
        }
    }
}