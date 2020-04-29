// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HostJsonFileConfigurationSourceTests
    {
        private readonly string _hostJsonWithBundles = "{\r\n  \"version\": \"2.0\",\r\n  \"extensionBundle\": {\r\n    \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\",\r\n    \"version\": \"[1.*, 2.0.0)\"\r\n  }\r\n}";
        private readonly string _defaultHostJson = "{\r\n  \"version\": \"2.0\"\r\n}";
        private readonly ScriptApplicationHostOptions _options;
        private readonly string _hostJsonFile;
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        public HostJsonFileConfigurationSourceTests()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");

            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            _options = new ScriptApplicationHostOptions
            {
                ScriptPath = rootPath
            };

            // delete any existing host.json
            _hostJsonFile = Path.Combine(rootPath, "host.json");
            if (File.Exists(_hostJsonFile))
            {
                File.Delete(_hostJsonFile);
            }
        }

        [Fact]
        public void MissingHostJson_CreatesDefaultFile()
        {
            Assert.False(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            BuildHostJsonConfiguration(testMetricsLogger);

            AreExpectedMetricsGenerated(testMetricsLogger);

            Assert.Equal(_hostJsonWithBundles, File.ReadAllText(_hostJsonFile));

            var log = _loggerProvider.GetAllLogMessages().Single(l => l.FormattedMessage == "No host configuration file found. Creating a default host.json file.");
            Assert.Equal(LogLevel.Information, log.Level);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\r\n}")]
        public void EmptyHostJson_CreatesDefaultFile(string json)
        {
            File.WriteAllText(_hostJsonFile, json);
            Assert.True(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            BuildHostJsonConfiguration(testMetricsLogger);

            AreExpectedMetricsGenerated(testMetricsLogger);

            Assert.Equal(_defaultHostJson, File.ReadAllText(_hostJsonFile));

            var log = _loggerProvider.GetAllLogMessages().Single(l => l.FormattedMessage == "Empty host configuration file found. Creating a default host.json file.");
            Assert.Equal(LogLevel.Information, log.Level);
        }

        [Theory]
        [InlineData("", "The host.json file is missing the required 'version' property.", "")]
        [InlineData("'version': '4.0',", "'4.0' is an invalid value for host.json 'version' property.", "")]
        [InlineData("'version': '3.0',", "'3.0' is an invalid value for host.json 'version' property.", "This does not correspond to the function runtime version")]
        public void InvalidVersionThrowsException(string versionLine, string errorStartsWith, string errorContains)
        {
            StringBuilder hostJsonContentBuilder = new StringBuilder(@"{");
            hostJsonContentBuilder.Append(versionLine);
            hostJsonContentBuilder.Append(@"'functions': [ 'FunctionA', 'FunctionB' ]}");
            string hostJsonContent = hostJsonContentBuilder.ToString();

            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            File.WriteAllText(_hostJsonFile, hostJsonContent);
            Assert.True(File.Exists(_hostJsonFile));

            var ex = Assert.Throws<HostConfigurationException>(() => BuildHostJsonConfiguration(testMetricsLogger));
            Assert.StartsWith(errorStartsWith, ex.Message);
            Assert.Contains(errorContains, ex.Message);
        }

        [Fact]
        public void ReadOnlyFileSystem_SkipsDefaultHostJsonCreation()
        {
            Assert.False(File.Exists(_hostJsonFile));

            var environment = new TestEnvironment(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "1" }
            });
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();
            IConfiguration config = BuildHostJsonConfiguration(testMetricsLogger, environment);
            AreExpectedMetricsGenerated(testMetricsLogger);
            var configList = config.AsEnumerable().ToList();
            Assert.Equal(config["AzureFunctionsJobHost:version"], "2.0");
            Assert.Equal(configList.Count, 2);
            Assert.True(configList.TrueForAll((k) => !k.Key.Contains("extensionBundle")));

            var log = _loggerProvider.GetAllLogMessages().Single(l => l.FormattedMessage == "No host configuration file found. Creating a default host.json file.");
            Assert.Equal(LogLevel.Information, log.Level);
        }

        [Fact]
        public void Initialize_Sanitizes_HostJsonLog()
        {
            // Turn off all logging. We shouldn't see any output.
            string hostJsonContent = @"
            {
                'version': '2.0',
                'functionTimeout': '00:05:00',
                'functions': [ 'FunctionA', 'FunctionB' ],
                'logging': {
                    'categoryFilter': {
                        'defaultLevel': 'Information'
                    }
                },
                'Values': {
                    'MyCustomValue': 'abc'
                }
            }";
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            File.WriteAllText(_hostJsonFile, hostJsonContent);

            BuildHostJsonConfiguration(testMetricsLogger);

            AreExpectedMetricsGenerated(testMetricsLogger);

            string hostJsonSanitized = @"
            {
                'version': '2.0',
                'functionTimeout': '00:05:00',
                'functions': [ 'FunctionA', 'FunctionB' ],
                'logging': {
                    'categoryFilter': {
                        'defaultLevel': 'Information'
                    }
                }
            }";

            // for formatting
            var hostJson = JObject.Parse(hostJsonSanitized);
            var logger = _loggerProvider.CreatedLoggers.Single(l => l.Category == LogCategories.Startup);
            var logMessage = logger.GetLogMessages().Single(l => l.FormattedMessage.StartsWith("Host configuration file read")).FormattedMessage;
            Assert.Equal($"Host configuration file read:{Environment.NewLine}{hostJson}", logMessage);
        }

        private IConfiguration BuildHostJsonConfiguration(TestMetricsLogger testMetricsLogger, IEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var configSource = new HostJsonFileConfigurationSource(_options, environment, loggerFactory, testMetricsLogger);

            var configurationBuilder = new ConfigurationBuilder()
                .Add(configSource);

            return configurationBuilder.Build();
        }

        private bool AreExpectedMetricsGenerated(TestMetricsLogger metricsLogger)
        {
            return metricsLogger.EventsBegan.Contains(MetricEventNames.LoadHostConfigurationSource) && metricsLogger.EventsEnded.Contains(MetricEventNames.LoadHostConfigurationSource)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.LoadHostConfiguration) && metricsLogger.EventsEnded.Contains(MetricEventNames.LoadHostConfiguration)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.InitializeHostConfiguration) && metricsLogger.EventsEnded.Contains(MetricEventNames.InitializeHostConfiguration);
        }
    }
}
