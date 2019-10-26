// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcWorkerConfigTests : IDisposable
    {
        private static string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private static string customRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private static string testLanguagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private static string testLanguage = "testLanguage";

        private readonly TestSystemRuntimeInformation _testSysRuntimeInfo = new TestSystemRuntimeInformation();
        private readonly TestEnvironment _testEnvironment;

        public RpcWorkerConfigTests()
        {
            _testEnvironment = new TestEnvironment();
        }

        public static IEnumerable<object[]> InvalidWorkerDescriptions
        {
            get
            {
                yield return new object[] { new RpcWorkerDescription() { Extensions = new List<string>() } };
                yield return new object[] { new RpcWorkerDescription() { Language = testLanguage } };
                yield return new object[] { new RpcWorkerDescription() { Language = testLanguage, Extensions = new List<string>() } };
            }
        }

        public static IEnumerable<object[]> ValidWorkerDescriptions
        {
            get
            {
                yield return new object[] { new RpcWorkerDescription() { Language = testLanguage, Extensions = new List<string>(), DefaultExecutablePath = "test.exe" } };
                yield return new object[] { new RpcWorkerDescription() { Language = testLanguage, Extensions = new List<string>(), DefaultExecutablePath = "test.exe", DefaultWorkerPath = RpcWorkerConfigTestUtilities.TestDefaultWorkerFile } };
                yield return new object[] { new RpcWorkerDescription() { Language = testLanguage, Extensions = new List<string>(), DefaultExecutablePath = "test.exe", DefaultWorkerPath = RpcWorkerConfigTestUtilities.TestDefaultWorkerFile, Arguments = new List<string>() } };
            }
        }

        public void Dispose()
        {
            _testEnvironment.Clear();
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderWithArguments()
        {
            var expectedArguments = new string[] { "-v", "verbose" };
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, expectedArguments) };
            var testLogger = new TestLogger(testLanguage);
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            IEnumerable<RpcWorkerConfig> workerConfigs = TestReadWorkerProviderFromConfig(configs, testLogger, testMetricsLogger);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);

            RpcWorkerConfig worker = workerConfigs.FirstOrDefault();
            Assert.True(expectedArguments.SequenceEqual(worker.Description.Arguments.ToArray()));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderNoArguments()
        {
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, new string[0]) };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);
            Assert.Equal(Path.Combine(rootPath, testLanguage, $"{RpcWorkerConfigTestUtilities.TestWorkerPathInWorkerConfig}.{testLanguage}"), workerConfigs.Single().Description.DefaultWorkerPath);
            RpcWorkerConfig worker = workerConfigs.FirstOrDefault();
            Assert.True(worker.Description.Arguments.Count == 0);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ArgumentsFromSettings()
        {
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, new string[0]) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{WorkerConstants.WorkerDescriptionArguments}"] = "--inspect=5689  --no-deprecation"
            };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger, null, keyValuePairs);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);
            Assert.Equal(Path.Combine(rootPath, testLanguage, $"{RpcWorkerConfigTestUtilities.TestWorkerPathInWorkerConfig}.{testLanguage}"), workerConfigs.Single().Description.DefaultWorkerPath);
            RpcWorkerConfig worker = workerConfigs.FirstOrDefault();
            Assert.True(worker.Description.Arguments.Count == 2);
            Assert.True(worker.Description.Arguments.Contains("--inspect=5689"));
            Assert.True(worker.Description.Arguments.Contains("--no-deprecation"));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_EmptyWorkerPath()
        {
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, new string[0], false, string.Empty, true) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{WorkerConstants.WorkerDescriptionArguments}"] = "--inspect=5689  --no-deprecation"
            };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);
            Assert.Null(workerConfigs.Single().Description.DefaultWorkerPath);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_Concatenate_ArgsFromSettings_ArgsFromWorkerConfig()
        {
            string[] argsFromConfig = new string[] { "--expose-http2", "--no-deprecation" };
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, argsFromConfig) };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{WorkerConstants.WorkerDescriptionArguments}"] = "--inspect=5689"
            };
            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger, null, keyValuePairs);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);
            RpcWorkerConfig workerConfig = workerConfigs.Single();
            Assert.Equal(Path.Combine(rootPath, testLanguage, $"{RpcWorkerConfigTestUtilities.TestWorkerPathInWorkerConfig}.{testLanguage}"), workerConfig.Description.DefaultWorkerPath);
            Assert.True(workerConfig.Description.Arguments.Count == 3);
            Assert.True(workerConfig.Description.Arguments.Contains("--inspect=5689"));
            Assert.True(workerConfig.Description.Arguments.Contains("--no-deprecation"));
            Assert.True(workerConfig.Description.Arguments.Contains("--expose-http2"));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_InvalidConfigFile()
        {
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, new string[0], true) };
            var testLogger = new TestLogger(testLanguage);
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var workerConfigs = TestReadWorkerProviderFromConfig(configs, testLogger, testMetricsLogger);
            AreRequiredMetricsEmitted(testMetricsLogger);
            var logs = testLogger.GetLogMessages();
            var errorLog = logs.Where(log => log.Level == LogLevel.Error).FirstOrDefault();
            Assert.NotNull(errorLog);
            Assert.NotNull(errorLog.Exception);
            Assert.True(errorLog.FormattedMessage.Contains("Failed to initialize"));
            Assert.Empty(workerConfigs);
        }

        [Fact]
        public void ReadWorkerProviderFromAppSetting()
        {
            var testConfig = MakeTestConfig(testLanguage, new string[0]);
            var configs = new List<TestRpcWorkerConfig>() { testConfig };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            RpcWorkerConfigTestUtilities.CreateWorkerFolder(customRootPath, testConfig);
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{WorkerConstants.WorkerDirectorySectionName}"] = Path.Combine(customRootPath, testLanguage)
            };

            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger, null, keyValuePairs);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);
            RpcWorkerConfig workerConfig = workerConfigs.Single();
            Assert.Equal(Path.Combine(customRootPath, testLanguage, $"{RpcWorkerConfigTestUtilities.TestWorkerPathInWorkerConfig}.{testLanguage}"), workerConfig.Description.DefaultWorkerPath);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_InvalidWorker()
        {
            var testConfig = MakeTestConfig(testLanguage, new string[0]);
            var configs = new List<TestRpcWorkerConfig>() { testConfig };
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            RpcWorkerConfigTestUtilities.CreateWorkerFolder(customRootPath, testConfig, false);
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{WorkerConstants.WorkerDirectorySectionName}"] = customRootPath
            };

            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger, null, keyValuePairs);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Empty(workerConfigs);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_AddProfile_ReturnsDefaultDescription()
        {
            var expectedArguments = new string[] { "-v", "verbose" };
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, expectedArguments, false, "TestProfile") };
            var testLogger = new TestLogger(testLanguage);
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            IEnumerable<RpcWorkerConfig> workerConfigs = TestReadWorkerProviderFromConfig(configs, testLogger, testMetricsLogger);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);

            RpcWorkerConfig worker = workerConfigs.FirstOrDefault();
            Assert.Equal(RpcWorkerConfigTestUtilities.TestDefaultExecutablePath, worker.Description.DefaultExecutablePath);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_OverrideDefaultExePath()
        {
            var configs = new List<TestRpcWorkerConfig>() { MakeTestConfig(testLanguage, new string[0], false, WorkerConstants.WorkerDescriptionAppServiceEnvProfileName) };
            var testLogger = new TestLogger(testLanguage);
            var testExePath = "./mySrc/myIndex";
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{WorkerConstants.WorkerDescriptionDefaultExecutablePath}"] = testExePath
            };
            var workerConfigs = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), testMetricsLogger, null, keyValuePairs, true);
            AreRequiredMetricsEmitted(testMetricsLogger);
            Assert.Single(workerConfigs);

            RpcWorkerConfig worker = workerConfigs.FirstOrDefault();
            Assert.Equal(testExePath, worker.Description.DefaultExecutablePath);
        }

        [Theory]
        [MemberData(nameof(InvalidWorkerDescriptions))]
        public void InvalidWorkerDescription_Throws(WorkerDescription workerDescription)
        {
            var testLogger = new TestLogger(testLanguage);

            Assert.Throws<ValidationException>(() => workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), testLogger));
        }

        [Theory]
        [MemberData(nameof(ValidWorkerDescriptions))]
        public void ValidateWorkerDescription_Succeeds(WorkerDescription workerDescription)
        {
            var testLogger = new TestLogger(testLanguage);

            try
            {
                RpcWorkerConfigTestUtilities.CreateTestWorkerFileInCurrentDir();
                workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), testLogger);
            }
            finally
            {
                RpcWorkerConfigTestUtilities.DeleteTestWorkerFileInCurrentDir();
            }
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("DotNet")]
        [InlineData("dotnet.exe")]
        [InlineData("DOTNET.EXE")]
        public void ValidateWorkerDescription_ResolvesDotNetDefaultWorkerExecutablePath_WhenExpectedFileExists(
            string defaultExecutablePath)
        {
            var testLogger = new TestLogger(testLanguage);

            var expectedExecutablePath =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")
                    : defaultExecutablePath;

            var workerDescription = new RpcWorkerDescription
            {
                Language = testLanguage,
                Extensions = new List<string>(),
                DefaultExecutablePath = defaultExecutablePath,
                FileExists = path =>
                                {
                                    Assert.Equal(expectedExecutablePath, path);
                                    return true;
                                }
            };

            workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), testLogger);
            Assert.Equal(expectedExecutablePath, workerDescription.DefaultExecutablePath);
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("DotNet")]
        [InlineData("dotnet.exe")]
        [InlineData("DOTNET.EXE")]
        public void ValidateWorkerDescription_DoesNotModifyDefaultWorkerExecutablePathAndWarns_WhenExpectedFileDoesNotExist(
            string defaultExecutablePath)
        {
            var testLogger = new TestLogger(testLanguage);

            var expectedExecutablePath =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")
                    : defaultExecutablePath;

            var workerDescription = new RpcWorkerDescription
            {
                Language = testLanguage,
                Extensions = new List<string>(),
                DefaultExecutablePath = defaultExecutablePath,
                FileExists = path =>
                                {
                                    Assert.Equal(expectedExecutablePath, path);
                                    return false;
                                }
            };

            workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), testLogger);
            Assert.Equal(defaultExecutablePath, workerDescription.DefaultExecutablePath);
            Assert.True(testLogger.GetLogMessages().Any(message => message.Level == LogLevel.Warning
                                                                   && message.FormattedMessage.Contains(defaultExecutablePath)
                                                                   && message.FormattedMessage.Contains(expectedExecutablePath)));
        }

        [Theory]
        [InlineData(@"D:\CustomExecutableFolder\dotnet")]
        [InlineData(@"/CustomExecutableFolder/dotnet")]
        [InlineData("AnythingElse")]
        public void ValidateWorkerDescription_DoesNotModifyDefaultWorkerExecutablePath_WhenDoesNotStrictlyMatchDotNet(
            string defaultExecutablePath)
        {
            var testLogger = new TestLogger(testLanguage);

            var workerDescription = new RpcWorkerDescription
            {
                Language = testLanguage,
                Extensions = new List<string>(),
                DefaultExecutablePath = defaultExecutablePath,
                FileExists = path =>
                                {
                                    Assert.True(false, "FileExists should not be called");
                                    return false;
                                }
            };

            workerDescription.ApplyDefaultsAndValidate(Directory.GetCurrentDirectory(), testLogger);
            Assert.Equal(defaultExecutablePath, workerDescription.DefaultExecutablePath);
        }

        private IEnumerable<RpcWorkerConfig> TestReadWorkerProviderFromConfig(IEnumerable<TestRpcWorkerConfig> configs, ILogger testLogger, TestMetricsLogger testMetricsLogger, string language = null, Dictionary<string, string> keyValuePairs = null, bool appSvcEnv = false)
        {
            Mock<IEnvironment> mockEnvironment = new Mock<IEnvironment>();
            var workerPathSection = $"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}";
            try
            {
                foreach (var workerConfig in configs)
                {
                    RpcWorkerConfigTestUtilities.CreateWorkerFolder(rootPath, workerConfig);
                }

                IConfigurationRoot config = TestConfigBuilder(workerPathSection, keyValuePairs);

                var scriptHostOptions = new ScriptJobHostOptions();
                var scriptSettingsManager = new ScriptSettingsManager(config);
                var configFactory = new RpcWorkerConfigFactory(config, testLogger, _testSysRuntimeInfo, _testEnvironment, testMetricsLogger);
                if (appSvcEnv)
                {
                    var testEnvVariables = new Dictionary<string, string>
                    {
                        { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" },
                    };
                    using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
                    {
                        configFactory.BuildWorkerProviderDictionary();
                        return configFactory.GetConfigs();
                    }
                }
                configFactory.BuildWorkerProviderDictionary();
                return configFactory.GetConfigs();
            }
            finally
            {
                RpcWorkerConfigTestUtilities.DeleteTestDir(rootPath);
                RpcWorkerConfigTestUtilities.DeleteTestDir(customRootPath);
            }
        }

        private static IConfigurationRoot TestConfigBuilder(string workerPathSection, Dictionary<string, string> keyValuePairs = null)
        {
            var configBuilderData = new Dictionary<string, string>
            {
                [workerPathSection] = rootPath
            };
            if (keyValuePairs != null)
            {
                configBuilderData.AddRange(keyValuePairs);
            }
            var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                                .AddInMemoryCollection(configBuilderData);
            var config = configBuilder.Build();
            return config;
        }

        private static TestRpcWorkerConfig MakeTestConfig(string language, string[] arguments, bool invalid = false, string addAppSvcProfile = "", bool emptyWorkerPath = false)
        {
            string json = RpcWorkerConfigTestUtilities.GetTestWorkerConfig(language, arguments, invalid, addAppSvcProfile, emptyWorkerPath).ToString();
            return new TestRpcWorkerConfig()
            {
                Json = json,
                Language = language,
            };
        }

        private bool AreRequiredMetricsEmitted(TestMetricsLogger metricsLogger)
        {
            bool hasBegun = false;
            bool hasEnded = false;
            foreach (string begin in metricsLogger.EventsBegan)
            {
                if (begin.Contains(MetricEventNames.AddProvider.Substring(0, MetricEventNames.AddProvider.IndexOf('{'))))
                {
                    hasBegun = true;
                    break;
                }
            }
            foreach (string end in metricsLogger.EventsEnded)
            {
                if (end.Contains(MetricEventNames.AddProvider.Substring(0, MetricEventNames.AddProvider.IndexOf('{'))))
                {
                    hasEnded = true;
                    break;
                }
            }
            return hasBegun && hasEnded && (metricsLogger.EventsBegan.Contains(MetricEventNames.GetConfigs) && metricsLogger.EventsEnded.Contains(MetricEventNames.GetConfigs));
        }
    }
}
