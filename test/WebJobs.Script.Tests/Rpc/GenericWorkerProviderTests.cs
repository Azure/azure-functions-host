// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(null, new List<string>()));
            Assert.Throws<ArgumentNullException>(() => new GenericWorkerProvider(new WorkerDescription(), null));
        }

        [Fact]
        public void GetDescription_ReturnsDescription()
        {
            var workerDescription = new WorkerDescription();
            var arguments = new List<string>();
            var provider = new GenericWorkerProvider(workerDescription, arguments);

            Assert.Equal(workerDescription, provider.GetDescription());
        }

        [Fact]
        public void TryConfigureArguments_ReturnsTrue()
        {
            var provider = new GenericWorkerProvider(new WorkerDescription(), new List<string>());
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
            var providers = TestReadWorkerProviderFromConfig(language, json, arguments, mockLogger);

            mockLogger.Verify(x => x.Log<object>(
               It.IsAny<LogLevel>(),
               It.IsAny<EventId>(),
               It.IsAny<object>(),
               It.IsAny<Exception>(),
               It.IsAny<Func<object, Exception, string>>()), Times.Never());

            Assert.Single(providers);
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
            var providers = TestReadWorkerProviderFromConfig(language, json, arguments, mockLogger);

            mockLogger.Verify(x => x.Log<object>(
               It.IsAny<LogLevel>(),
               It.IsAny<EventId>(),
               It.IsAny<object>(),
               It.IsAny<Exception>(),
               It.IsAny<Func<object, Exception, string>>()), Times.Never());

            Assert.Single(providers);
        }

        [Fact]
        public void ReadWorkerProviderFromConfig_BadConfigFile()
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

            string json = "garbage";

            var mockLogger = new Mock<ILogger<object>>(MockBehavior.Loose);
            mockLogger.Setup(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()));

            // Creates temp directory w/ worker.config.json and runs ReadWorkerProviderFromConfig
            var providers = TestReadWorkerProviderFromConfig(language, json, arguments, mockLogger);

            mockLogger.Verify(x => x.Log<object>(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()), Times.Once());

            Assert.Empty(providers);
        }

        private IEnumerable<IWorkerProvider> TestReadWorkerProviderFromConfig(string language, string json, string[] arguments, Mock<ILogger<object>> mockLogger)
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
                return GenericWorkerProvider.ReadWorkerProviderFromConfig(scriptHostConfig, mockLogger.Object, scriptSettingsManager);
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }
    }
}
