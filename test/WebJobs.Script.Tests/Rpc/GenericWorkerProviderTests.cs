// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
            Assert.True(provider.TryConfigureArguments(args, null, null));
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
        public void ReadWorkerProviderFromConfig_SingleLanguage()
        {
            var language1 = "language1";
            var language2 = "language2";
            var language1Config = MakeTestConfig(language1, new string[0]);
            var language2Config = MakeTestConfig(language2, new string[0]);
            var configs = new List<TestLanguageWorkerConfig>() { language1Config, language2Config };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage), language: language1);
            Assert.Single(providers);
            Assert.Equal(language1, providers.Single().GetDescription().Language);
        }

        [Fact]
        public void ReadWorkerProviderFromAppSetting()
        {
            var testConfig = MakeTestConfig(testLanguage, new string[0]);
            var configs = new List<TestLanguageWorkerConfig>() { testConfig };
            CreateWorkerFolder(customRootPath, testConfig);
            Dictionary<string, string> keyValuePairs = new Dictionary<string, string>
            {
                [$"{LanguageWorkerConstants.LanguageWorkersSectionName}:{testLanguage}:{LanguageWorkerConstants.WorkerDirectorySectionName}"] = customRootPath
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

        private IEnumerable<IWorkerProvider> TestReadWorkerProviderFromConfig(IEnumerable<TestLanguageWorkerConfig> configs, ILogger testLogger, string language = null, Dictionary<string, string> keyValuePairs = null)
        {
            var workerPathSection = $"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.WorkersDirectorySectionName}";
            try
            {
                foreach (var workerConfig in configs)
                {
                    CreateWorkerFolder(rootPath, workerConfig);
                }

                IConfigurationRoot config = TestConfigBuilder(workerPathSection, keyValuePairs);

                var scriptHostConfig = new ScriptHostConfiguration();
                var scriptSettingsManager = new ScriptSettingsManager(config);
                var configFactory = new WorkerConfigFactory(config, testLogger);

                return configFactory.GetWorkerProviders(testLogger, scriptSettingsManager, language: language);
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

        private static TestLanguageWorkerConfig MakeTestConfig(string language, string[] arguments, bool invalid = false)
        {
            string json = GetTestWorkerConfig(language, arguments, invalid).ToString();
            return new TestLanguageWorkerConfig()
            {
                Json = json,
                Language = language,
            };
        }

        private static JObject GetTestWorkerConfig(string language, string[] arguments, bool invalid)
        {
            var description = new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = $"{testWorkerPathInWorkerConfig}.{language}",
                Language = language,
                Extension = $".{language}",
                Arguments = arguments.ToList()
        };

            JObject config = new JObject();
            config["Description"] = JObject.FromObject(description);
            if (invalid)
            {
                config["Description"] = "invalidWorkerConfig";
            }

            return config;
        }

        private class TestLanguageWorkerConfig
        {
            public string Language { get; set; }

            public string Json { get; set; }
        }
    }
}
