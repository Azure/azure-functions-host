// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class GenericWorkerProviderTests
    {
        private static string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private static string customRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private static string testWorkerPathInWorkerConfig = "./src/index";
        private static string testLanguagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private static string testLanguage = "testLanguage";

        public static IEnumerable<object[]> InvalidWorkerDescriptions
        {
            get
            {
                yield return new object[] { new WorkerDescription() { Extensions = new List<string>() } };
                yield return new object[] { new WorkerDescription() { Language = testLanguage } };
                yield return new object[] { new WorkerDescription() { Language = testLanguage, Extensions = new List<string>() } };
            }
        }

        public static IEnumerable<object[]> ValidWorkerDescriptions
        {
            get
            {
                yield return new object[] { new WorkerDescription() { Language = testLanguage, Extensions = new List<string>(), DefaultExecutablePath = "test.exe" } };
                yield return new object[] { new WorkerDescription() { Language = testLanguage, Extensions = new List<string>(), DefaultExecutablePath = "test.exe", DefaultWorkerPath = testWorkerPathInWorkerConfig } };
                yield return new object[] { new WorkerDescription() { Language = testLanguage, Extensions = new List<string>(), DefaultExecutablePath = "test.exe", DefaultWorkerPath = testWorkerPathInWorkerConfig, Arguments = new List<string>() } };
            }
        }

        [Fact]
        public void Constructor_ThrowsNullArgument()
        {
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(null, string.Empty));
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(new WorkerDescription(), null));
        }

        [Fact]
        public void GetDescription_ReturnsDescription()
        {
            var workerDescription = new WorkerDescription();
            var provider = new GenericWorkerProvider(workerDescription, string.Empty);
            Assert.Equal(workerDescription, provider.GetDescription());
        }

        [Fact]
        public void TryConfigureArguments_ReturnsTrue()
        {
            var provider = new GenericWorkerProvider(new WorkerDescription(), string.Empty);
            var args = new WorkerProcessArguments();
            Assert.True(provider.TryConfigureArguments(args, null));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderWithArguments()
        {
            var expectedArguments = new string[] { "-v", "verbose" };
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, expectedArguments) };
            var testLogger = new TestLogger(testLanguage);

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            IEnumerable<IWorkerProvider> providers = TestReadWorkerProviderFromConfig(configs, testLogger);
            Assert.Single(providers);

            IWorkerProvider worker = providers.FirstOrDefault();
            Assert.True(expectedArguments.SequenceEqual(worker.GetDescription().Arguments.ToArray()));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderNoArguments()
        {
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, new string[0]) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage));
            Assert.Single(providers);
            Assert.Equal(Path.Combine(rootPath, testLanguage, $"{testWorkerPathInWorkerConfig}.{testLanguage}"), providers.Single().GetDescription().GetWorkerPath());
            IWorkerProvider worker = providers.FirstOrDefault();
            Assert.True(worker.GetDescription().Arguments.Count == 0);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ArgumentsFromSettings()
        {
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, new string[0]) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDescriptionArguments}"] = "--inspect=5689  --no-deprecation"
            };
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), null, keyValuePairs);
            Assert.Single(providers);
            Assert.Equal(Path.Combine(rootPath, testLanguage, $"{testWorkerPathInWorkerConfig}.{testLanguage}"), providers.Single().GetDescription().GetWorkerPath());
            IWorkerProvider worker = providers.FirstOrDefault();
            Assert.True(worker.GetDescription().Arguments.Count == 2);
            Assert.True(worker.GetDescription().Arguments.Contains("--inspect=5689"));
            Assert.True(worker.GetDescription().Arguments.Contains("--no-deprecation"));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_EmptyWorkerPath()
        {
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, new string[0], false, string.Empty, true) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDescriptionArguments}"] = "--inspect=5689  --no-deprecation"
            };
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage));
            Assert.Single(providers);
            Assert.True(providers.Single().GetDescription().GetWorkerPath() == null);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_Concatenate_ArgsFromSettings_ArgsFromWorkerConfig()
        {
            string[] argsFromConfig = new string[] { "--expose-http2", "--no-deprecation" };
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, argsFromConfig) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDescriptionArguments}"] = "--inspect=5689"
            };
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), null, keyValuePairs);
            Assert.Single(providers);
            IWorkerProvider workerProvider = providers.Single();
            Assert.Equal(Path.Combine(rootPath, testLanguage, $"{testWorkerPathInWorkerConfig}.{testLanguage}"), workerProvider.GetDescription().GetWorkerPath());
            Assert.True(workerProvider.GetDescription().Arguments.Count == 3);
            Assert.True(workerProvider.GetDescription().Arguments.Contains("--inspect=5689"));
            Assert.True(workerProvider.GetDescription().Arguments.Contains("--no-deprecation"));
            Assert.True(workerProvider.GetDescription().Arguments.Contains("--expose-http2"));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_InvalidConfigFile()
        {
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, new string[0], true) };
            var testLogger = new TestLogger(testLanguage);
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var providers = TestReadWorkerProviderFromConfig(configs, testLogger);
            var logs = testLogger.GetLogMessages();
            var errorLog = logs.Where(log => log.Level == LogLevel.Error).FirstOrDefault();
            Assert.NotNull(errorLog);
            Assert.NotNull(errorLog.Exception);
            Assert.True(errorLog.FormattedMessage.Contains("Failed to initialize"));
            Assert.Empty(providers);
        }

        [Fact]
        public void ReadWorkerProviderFromAppSetting()
        {
            var testConfig = MakeTestConfig(testLanguage, new string[0]);
            var configs = new List<TestLanguageWorkerConfig>() { testConfig };
            CreateWorkerFolder(customRootPath, testConfig);
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDirectorySectionName}"] = Path.Combine(customRootPath, testLanguage)
            };

            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), null, keyValuePairs);
            Assert.Single(providers);
            IWorkerProvider workerProvider = providers.Single();
            Assert.Equal(Path.Combine(customRootPath, testLanguage, $"{testWorkerPathInWorkerConfig}.{testLanguage}"), workerProvider.GetDescription().GetWorkerPath());
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_InvalidWorker()
        {
            var testConfig = MakeTestConfig(testLanguage, new string[0]);
            var configs = new List<TestLanguageWorkerConfig>() { testConfig };
            CreateWorkerFolder(customRootPath, testConfig, false);
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDirectorySectionName}"] = customRootPath
            };

            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), null, keyValuePairs);
            Assert.Empty(providers);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_AddProfile_ReturnsDefaultDescription()
        {
            var expectedArguments = new string[] { "-v", "verbose" };
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, expectedArguments, false, "TestProfile") };
            var testLogger = new TestLogger(testLanguage);

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            IEnumerable<IWorkerProvider> providers = TestReadWorkerProviderFromConfig(configs, testLogger);
            Assert.Single(providers);

            IWorkerProvider worker = providers.FirstOrDefault();
            Assert.Equal("foopath", worker.GetDescription().DefaultExecutablePath);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_OverrideDefaultExePath()
        {
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, new string[0], false, LanguageWorkerConstants.WorkerDescriptionAppServiceEnvProfileName) };
            var testLogger = new TestLogger(testLanguage);
            var testExePath = "./mySrc/myIndex";
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDescriptionDefaultExecutablePath}"] = testExePath
            };
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), null, keyValuePairs, true);
            Assert.Single(providers);

            IWorkerProvider worker = providers.FirstOrDefault();
            Assert.Equal(testExePath, worker.GetDescription().DefaultExecutablePath);
        }

        [Theory]
        [MemberData(nameof(InvalidWorkerDescriptions))]
        public void InvalidWorkerDescription_Throws(WorkerDescription workerDescription)
        {
            Assert.Throws<ValidationException>(() => workerDescription.Validate());
        }

        [Theory]
        [MemberData(nameof(ValidWorkerDescriptions))]
        public void ValidateWorkerDescription_Succeeds(WorkerDescription workerDescription)
        {
            workerDescription.Validate();
        }

        private IEnumerable<IWorkerProvider> TestReadWorkerProviderFromConfig(IEnumerable<TestLanguageWorkerConfig> configs, ILogger testLogger, string language = null, Dictionary<string, string> keyValuePairs = null, bool appSvcEnv = false)
        {
            var workerPathSection = $"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.WorkersDirectorySectionName}";
            try
            {
                foreach (var workerConfig in configs)
                {
                    CreateWorkerFolder(rootPath, workerConfig);
                }

                IConfigurationRoot config = TestConfigBuilder(workerPathSection, keyValuePairs);

                var scriptHostOptions = new ScriptJobHostOptions();
                var scriptSettingsManager = new ScriptSettingsManager(config);
                var configFactory = new WorkerConfigFactory(config, testLogger);
                if (appSvcEnv)
                {
                    var testEnvVariables = new Dictionary<string, string>
                    {
                        { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" },
                    };
                    using (var variables = new TestScopedSettings(scriptSettingsManager, testEnvVariables))
                    {
                        configFactory.BuildWorkerProviderDictionary();
                        return configFactory.WorkerProviders;
                    }
                }
                configFactory.BuildWorkerProviderDictionary();
                return configFactory.WorkerProviders;
            }
            finally
            {
                DeleteTestDir(rootPath);
                DeleteTestDir(customRootPath);
            }
        }

        private static void DeleteTestDir(string testDir)
        {
            if (Directory.Exists(testDir))
            {
                try
                {
                    Directory.Delete(testDir, true);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        private static void CreateWorkerFolder(string testDir, TestLanguageWorkerConfig workerConfig, bool createTestWorker = true)
        {
            string workerPath = Path.Combine(testDir, workerConfig.Language);
            Directory.CreateDirectory(workerPath);
            File.WriteAllText(Path.Combine(workerPath, LanguageWorkerConstants.WorkerConfigFileName), workerConfig.Json);
            if (createTestWorker)
            {
                Directory.CreateDirectory(Path.Combine(workerPath, $"{testWorkerPathInWorkerConfig}"));
                File.WriteAllText(Path.Combine(workerPath, $"{testWorkerPathInWorkerConfig}.{workerConfig.Language}"), "test worker");
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

        private static TestLanguageWorkerConfig MakeTestConfig(string language, string[] arguments, bool invalid = false, string addAppSvcProfile = "", bool emptyWorkerPath = false)
        {
            string json = GetTestWorkerConfig(language, arguments, invalid, addAppSvcProfile, emptyWorkerPath).ToString();
            return new TestLanguageWorkerConfig()
            {
                Json = json,
                Language = language,
            };
        }

        private static JObject GetTestWorkerConfig(string language, string[] arguments, bool invalid, string profileName, bool emptyWorkerPath = false)
        {
            WorkerDescription description = GetTestDefaultWorkerDescription(language, arguments);

            JObject config = new JObject();
            config[LanguageWorkerConstants.WorkerDescription] = JObject.FromObject(description);

            if (!string.IsNullOrEmpty(profileName))
            {
                var appSvcDescription = new WorkerDescription()
                {
                    DefaultExecutablePath = "myFooPath",
                };

                JObject profiles = new JObject();
                profiles[profileName] = JObject.FromObject(appSvcDescription);
                config[LanguageWorkerConstants.WorkerDescriptionProfiles] = profiles;
            }

            if (invalid)
            {
                config[LanguageWorkerConstants.WorkerDescription] = "invalidWorkerConfig";
            }

            if (emptyWorkerPath)
            {
                config[LanguageWorkerConstants.WorkerDescription][LanguageWorkerConstants.WorkerDescriptionDefaultWorkerPath] = null;
            }

            return config;
        }

        private static WorkerDescription GetTestDefaultWorkerDescription(string language, string[] arguments)
        {
            return new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = $"{testWorkerPathInWorkerConfig}.{language}",
                Language = language,
                Extensions = new List<string> { $".{language}" },
                Arguments = arguments.ToList()
            };
        }

        private class TestLanguageWorkerConfig
        {
            public string Language { get; set; }

            public string Json { get; set; }
        }
    }
}
