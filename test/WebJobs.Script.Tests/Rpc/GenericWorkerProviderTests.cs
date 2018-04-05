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
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class GenericWorkerProviderTests
    {
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
            string language = "test";
            var description = new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = "./src/index.test",
                Language = "test",
                Extension = ".test"
            };

            var arguments = new string[] { "-v", "verbose" };

            JObject config = new JObject();
            config["Description"] = JObject.FromObject(description);
            config["Arguments"] = JArray.FromObject(arguments);

            string json = config.ToString();

            var mockLogger = new Mock<ILogger<object>>(MockBehavior.Loose);
            mockLogger.Setup(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()));

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            (var workerPath, var providers) = TestReadWorkerProviderFromConfig(language, json, arguments, mockLogger);

            mockLogger.Verify(x => x.Log<object>(
               It.IsAny<LogLevel>(),
               It.IsAny<EventId>(),
               It.IsAny<object>(),
               It.IsAny<Exception>(),
               It.IsAny<Func<object, Exception, string>>()), Times.Never());

            Assert.Single(providers);

            Assert.Equal(workerPath, providers.Single().GetWorkerDirectoryPath());
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_ReturnsProviderNoArguments()
        {
            string language = "test";
            var description = new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = "./src/index.test",
                Language = "test",
                Extension = ".test"
            };

            var arguments = new string[] { };

            JObject config = new JObject();
            config["Description"] = JObject.FromObject(description);
            config["Arguments"] = JArray.FromObject(arguments);

            string json = config.ToString();

            var mockLogger = new Mock<ILogger<object>>(MockBehavior.Loose);
            mockLogger.Setup(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()));

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            (var workerPath, var providers) = TestReadWorkerProviderFromConfig(language, json, arguments, mockLogger);

            mockLogger.Verify(x => x.Log<object>(
               It.IsAny<LogLevel>(),
               It.IsAny<EventId>(),
               It.IsAny<object>(),
               It.IsAny<Exception>(),
               It.IsAny<Func<object, Exception, string>>()), Times.Never());

            Assert.Single(providers);

            Assert.Equal(workerPath, providers.Single().GetWorkerDirectoryPath());
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_BadConfigFile()
        {
            string language = "test";

            string json = "garbage";

            var mockLogger = new Mock<ILogger<object>>(MockBehavior.Loose);
            mockLogger.Setup(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()));

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            (var workerPath, var providers) = TestReadWorkerProviderFromConfig(language, json, null, mockLogger);

            mockLogger.Verify(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()), Times.Once());

            Assert.Empty(providers);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_SingleLanguage()
        {
            var language1 = "language1";
            var language2 = "language2";

            var language1Config = MakeTestConfig(language1);
            var language2Config = MakeTestConfig(language2);

            var configs = new List<TestLanguageWorkerConfig>() { language1Config, language2Config };

            var mockLogger = new Mock<ILogger<object>>(MockBehavior.Loose);
            mockLogger.Setup(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()));

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            (var workerPath, var providers) = TestReadWorkerProviderFromConfig(configs, mockLogger, language: language1);

            mockLogger.Verify(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()), Times.Never());

            Assert.Single(providers);

            Assert.Equal(language1, providers.Single().GetDescription().Language);
        }

        private (string workerPath, IEnumerable<IWorkerProvider> providers) TestReadWorkerProviderFromConfig(string language, string json, string[] arguments, Mock<ILogger<object>> mockLogger)
        {
            string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string workerPath = Path.Combine(rootPath, language);
            try
            {
                Directory.CreateDirectory(workerPath);

                File.WriteAllText(Path.Combine(workerPath, ScriptConstants.WorkerConfigFileName), json);

                var scriptHostConfig = new ScriptHostConfiguration();
                var scriptSettingsManager = new ScriptSettingsManager();
                var settings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("workers:config:path", rootPath)
                };
                scriptSettingsManager.SetConfigurationFactory(() => new ConfigurationBuilder()
                    .AddInMemoryCollection(settings).Build());
                return (workerPath, GenericWorkerProvider.ReadWorkerProviderFromConfig(scriptHostConfig, mockLogger.Object, scriptSettingsManager));
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        private (string workerPath, IEnumerable<IWorkerProvider> providers) TestReadWorkerProviderFromConfig(IEnumerable<TestLanguageWorkerConfig> configs, Mock<ILogger<object>> mockLogger, string language = null)
        {
            string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                foreach (var config in configs)
                {
                    string workerPath = Path.Combine(rootPath, config.Language);
                    Directory.CreateDirectory(workerPath);

                    File.WriteAllText(Path.Combine(workerPath, ScriptConstants.WorkerConfigFileName), config.Json);
                }

                var scriptHostConfig = new ScriptHostConfiguration();
                var scriptSettingsManager = new ScriptSettingsManager();
                var settings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("workers:config:path", rootPath)
                };
                scriptSettingsManager.SetConfigurationFactory(() => new ConfigurationBuilder()
                    .AddInMemoryCollection(settings).Build());
                return (rootPath, GenericWorkerProvider.ReadWorkerProviderFromConfig(scriptHostConfig, mockLogger.Object, scriptSettingsManager, language: language));
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        private static TestLanguageWorkerConfig MakeTestConfig(string language)
        {
            var description = new WorkerDescription()
            {
                DefaultExecutablePath = "foopath",
                DefaultWorkerPath = $"./src/index.{language}",
                Language = language,
                Extension = $".{language}"
            };

            var arguments = new string[] { };

            JObject config = new JObject();
            config["Description"] = JObject.FromObject(description);
            config["Arguments"] = JArray.FromObject(arguments);

            string json = config.ToString();
            return new TestLanguageWorkerConfig()
            {
                Json = json,
                Language = language
            };
        }

        private class TestLanguageWorkerConfig
        {
            public string Language { get; set; }

            public string Json { get; set; }
        }
    }
}
