// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.AppCapabilities
{
    public class AppCapabilitiesOptionsSetupTests : IDisposable
    {
        private readonly string _rootPath;
        private readonly string _hostJsonFile;
        private readonly ScriptApplicationHostOptions _scriptOptions;
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public AppCapabilitiesOptionsSetupTests()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "AppCapabilitiesTests", Guid.NewGuid().ToString());

            if (!Directory.Exists(_rootPath))
            {
                Directory.CreateDirectory(_rootPath);
            }

            _scriptOptions = new ScriptApplicationHostOptions
            {
                ScriptPath = _rootPath
            };

            _hostJsonFile = Path.Combine(_rootPath, "host.json");
            if (File.Exists(_hostJsonFile))
            {
                File.Delete(_hostJsonFile);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        [Fact]
        public void Configure_HostJsonOnly_CapturesCapabilities()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:appCapabilities:capability1", "value1" },
                    { "AzureFunctionsJobHost:appCapabilities:capability2", "value2" }
                })
                .Build();

            var mockStore = new Mock<IAppCapabilitiesStore>();
            mockStore.Setup(s => s.Capabilities).Returns(new Dictionary<string, string>());

            var logger = new LoggerFactory().CreateLogger<AppCapabilitiesOptionsSetup>();
            var setup = new AppCapabilitiesOptionsSetup(configuration, mockStore.Object, logger);
            var options = new AppCapabilitiesOptions();

            setup.Configure(options);

            var optionsDict = (IDictionary<string, string>)options;
            Assert.Equal(2, optionsDict.Count);
            Assert.Equal("value1", optionsDict["capability1"]);
            Assert.Equal("value2", optionsDict["capability2"]);
        }

        [Fact]
        public void Configure_WorkerOnly_CapturesCapabilities()
        {
            var configuration = new ConfigurationBuilder().Build();

            var workerCapabilities = new Dictionary<string, string>
            {
                { "capability1", "workerValue1" },
                { "capability2", "workerValue2" }
            };
            var mockStore = new Mock<IAppCapabilitiesStore>();
            mockStore.Setup(s => s.Capabilities).Returns(workerCapabilities);

            var logger = new LoggerFactory().CreateLogger<AppCapabilitiesOptionsSetup>();
            var setup = new AppCapabilitiesOptionsSetup(configuration, mockStore.Object, logger);
            var options = new AppCapabilitiesOptions();

            setup.Configure(options);

            var optionsDict = (IDictionary<string, string>)options;
            Assert.Equal(2, optionsDict.Count);
            Assert.Equal("workerValue1", optionsDict["capability1"]);
            Assert.Equal("workerValue2", optionsDict["capability2"]);
        }

        [Fact]
        public void Configure_WorkerOverridesHostJson_WorkerWins()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:appCapabilities:capability1", "hostJsonValue" },
                    { "AzureFunctionsJobHost:appCapabilities:capability2", "hostJsonOnly" }
                })
                .Build();

            var workerCapabilities = new Dictionary<string, string>
            {
                { "capability1", "workerOverrideValue" },
                { "capability3", "workerOnlyValue" }
            };
            var mockStore = new Mock<IAppCapabilitiesStore>();
            mockStore.Setup(s => s.Capabilities).Returns(workerCapabilities);

            using var loggerFactory = new LoggerFactory();
            var loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);
            var logger = loggerFactory.CreateLogger<AppCapabilitiesOptionsSetup>();

            var setup = new AppCapabilitiesOptionsSetup(configuration, mockStore.Object, logger);
            var options = new AppCapabilitiesOptions();

            setup.Configure(options);

            var optionsDict = (IDictionary<string, string>)options;
            Assert.Equal(3, optionsDict.Count);
            Assert.Equal("workerOverrideValue", optionsDict["capability1"]);
            Assert.Equal("hostJsonOnly", optionsDict["capability2"]);
            Assert.Equal("workerOnlyValue", optionsDict["capability3"]);

            var logs = loggerProvider.GetAllLogMessages();
            Assert.Contains(logs, l => l.FormattedMessage.Contains("Duplicate capability key found") &&
                                      l.FormattedMessage.Contains("worker provided value"));
        }

        [Fact]
        public void Configure_CaseInsensitiveKeys_WorksCorrectly()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:appCapabilities:MyCapability", "hostJsonValue" }
                })
                .Build();

            var mockStore = new Mock<IAppCapabilitiesStore>();
            mockStore.Setup(s => s.Capabilities).Returns(new Dictionary<string, string>());

            var logger = new LoggerFactory().CreateLogger<AppCapabilitiesOptionsSetup>();
            var setup = new AppCapabilitiesOptionsSetup(configuration, mockStore.Object, logger);
            var options = new AppCapabilitiesOptions();

            setup.Configure(options);

            var optionsDict = (IDictionary<string, string>)options;
            Assert.True(optionsDict.ContainsKey("mycapability"));
            Assert.True(optionsDict.ContainsKey("MyCapability"));
            Assert.True(optionsDict.ContainsKey("MYCAPABILITY"));
            Assert.Equal("hostJsonValue", optionsDict["mycapability"]);
        }

        [Fact]
        public void Configure_PhysicalHostJsonFile_LoadsCapabilities_WorkerOverrides()
        {
            string hostJsonContent = @"{
                'version': '2.0',
                'appCapabilities': {
                    'fileCapability1': 'fileValue1',
                    'fileCapability2': 'fileValue2',
                    'sharedCapability': 'hostJsonValue'
                }
            }";

            var configuration = BuildHostJsonConfiguration(hostJsonContent);

            var workerCapabilities = new Dictionary<string, string>
            {
                { "sharedCapability", "workerValue" },
                { "workerCapability", "workerOnlyValue" }
            };
            var mockStore = new Mock<IAppCapabilitiesStore>();
            mockStore.Setup(s => s.Capabilities).Returns(workerCapabilities);

            using var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            var logger = loggerFactory.CreateLogger<AppCapabilitiesOptionsSetup>();

            var setup = new AppCapabilitiesOptionsSetup(configuration, mockStore.Object, logger);
            var options = new AppCapabilitiesOptions();

            setup.Configure(options);

            var optionsDict = (IDictionary<string, string>)options;
            Assert.Equal(4, optionsDict.Count);
            Assert.Equal("fileValue1", optionsDict["fileCapability1"]);
            Assert.Equal("fileValue2", optionsDict["fileCapability2"]);
            Assert.Equal("workerValue", optionsDict["sharedCapability"]);
            Assert.Equal("workerOnlyValue", optionsDict["workerCapability"]);

            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Contains(logs, l => l.FormattedMessage.Contains("Duplicate capability key found") &&
                                      l.FormattedMessage.Contains("worker provided value"));
        }

        [Fact]
        public void Configure_StoreNotInitialized_DoesNotThrow()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:appCapabilities:capability1", "hostValue1" },
                    { "AzureFunctionsJobHost:appCapabilities:capability2", "hostValue2" }
                })
                .Build();

            var mockStore = new Mock<IAppCapabilitiesStore>();
            mockStore.Setup(s => s.Capabilities).Throws(new InvalidOperationException("Capabilities have not been initialized."));

            var logger = new LoggerFactory().CreateLogger<AppCapabilitiesOptionsSetup>();
            var setup = new AppCapabilitiesOptionsSetup(configuration, mockStore.Object, logger);
            var options = new AppCapabilitiesOptions();

            setup.Configure(options);

            var optionsDict = (IDictionary<string, string>)options;
            Assert.Equal(2, optionsDict.Count);
            Assert.Equal("hostValue1", optionsDict["capability1"]);
            Assert.Equal("hostValue2", optionsDict["capability2"]);
        }

        private IConfiguration BuildHostJsonConfiguration(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var environment = new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            var testMetricsLogger = new TestMetricsLogger();

            var hostJsonConfigOptions = new HostJsonFileConfigurationOptions(environment, _scriptOptions);
            var configSource = new HostJsonFileConfigurationSource(hostJsonConfigOptions, loggerFactory, testMetricsLogger);

            return new ConfigurationBuilder()
                .Add(configSource)
                .Build();
        }
    }
}
