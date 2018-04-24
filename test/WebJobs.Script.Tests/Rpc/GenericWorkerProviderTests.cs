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
        private static string testLanguage = "testLanguage";

        [Fact]
        public void Constructor_ThrowsNullArgument()
        {
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(null, new List<string>(), string.Empty));
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(new WorkerDescription(), null, string.Empty));
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(new WorkerDescription(), new List<string>(), null));
        }

        [Fact]
        public void GetDescription_ReturnsDescription()
        {
            var workerDescription = new WorkerDescription();
            var arguments = new List<string>();
            var provider = new GenericWorkerProvider(workerDescription, arguments, string.Empty);
            Assert.Equal(workerDescription, provider.GetDescription());
        }

        [Fact]
        public void TryConfigureArguments_ReturnsTrue()
        {
            var provider = new GenericWorkerProvider(new WorkerDescription(), new List<string>(), string.Empty);
            var args = new ArgumentsDescription();
            Assert.True(provider.TryConfigureArguments(args, null, null));
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderWithArguments()
        {
            var arguments = new string[] { "-v", "verbose" };
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, arguments) };
            var testLogger = new TestLogger(testLanguage);

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var providers = TestReadWorkerProviderFromConfig(configs, testLogger);
            var logs = testLogger.GetLogMessages();

            Assert.Equal(3, testLogger.GetLogMessages().Count);
            Assert.Contains($"Loading all the worker providers from the default workers directory", testLogger.GetLogMessages()[0].FormattedMessage);
            Assert.Single(providers);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderNoArguments()
        {
            var configs = new List<TestLanguageWorkerConfig>() { MakeTestConfig(testLanguage, new string[0]) };
            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var providers = TestReadWorkerProviderFromConfig(configs, new TestLogger(testLanguage));
            Assert.Single(providers);
            Assert.Equal(Path.Combine(rootPath, testLanguage), providers.Single().GetWorkerDirectoryPath());
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

        private IEnumerable<IWorkerProvider> TestReadWorkerProviderFromConfig(IEnumerable<TestLanguageWorkerConfig> configs, ILogger testLogger, string language = null)
        {
            try
            {
                foreach (var workerConfig in configs)
                {
                    string workerPath = Path.Combine(rootPath, workerConfig.Language);
                    Directory.CreateDirectory(workerPath);
                    File.WriteAllText(Path.Combine(workerPath, ScriptConstants.WorkerConfigFileName), workerConfig.Json);
                }

                var configBuilder = ScriptSettingsManager.CreateDefaultConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["workers:config:path"] = rootPath
                    });
                var config = configBuilder.Build();

                var scriptHostConfig = new ScriptHostConfiguration();
                var scriptSettingsManager = new ScriptSettingsManager(config);

                return GenericWorkerProvider.ReadWorkerProviderFromConfig(scriptHostConfig, testLogger, scriptSettingsManager, language: language);
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        private static TestLanguageWorkerConfig MakeTestConfig(string language, string[] arguments, bool invalid = false)
        {
            var description = new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = $"./src/index.{language}",
                Language = language,
                Extension = $".{language}"
            };

            JObject config = new JObject();
            config["Description"] = JObject.FromObject(description);
            config["Arguments"] = JArray.FromObject(arguments);

            if (invalid)
            {
                config["Description"] = "invalidWorkerConfig";
            }

            string json = config.ToString();
            return new TestLanguageWorkerConfig()
            {
                Json = json,
                Language = language,
            };
        }

        private class TestLanguageWorkerConfig
        {
            public string Language { get; set; }

            public string Json { get; set; }
        }
    }
}
