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
        private readonly string _hostJsonWithBundles = "{\r\n  \"version\": \"2.0\",\r\n  \"extensionBundle\": {\r\n    \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\",\r\n    \"version\": \"[4.*, 5.0.0)\"\r\n  }\r\n}";
        private readonly string _hostJsonWithWorkFlowBundle = "{\r\n  \"version\": \"2.0\",\r\n  \"extensionBundle\": {\r\n    \"id\": \"Microsoft.Azure.Functions.ExtensionBundle.Workflows\",\r\n    \"version\": \"[1.*, 2.0.0)\"\r\n  }\r\n}";
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
        public void MissingHostJson_CreatesHostJson_withDefaultExtensionBundleId()
        {
            Assert.False(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            BuildHostJsonConfiguration(testMetricsLogger);

            AreExpectedMetricsGenerated(testMetricsLogger);

            Assert.Equal(_hostJsonWithBundles, File.ReadAllText(_hostJsonFile));

            var log = _loggerProvider.GetAllLogMessages().Single(l => l.FormattedMessage == "No host configuration file found. Creating a default host.json file.");
            Assert.Equal(LogLevel.Information, log.Level);
        }

        [Fact]
        public void MissingHostJson_CreatesHostJson_withWorkFlowExtensionBundleId()
        {
            var environment = new TestEnvironment(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AppKind, "workflowApp" }
            });

            Assert.False(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            BuildHostJsonConfiguration(testMetricsLogger, environment);

            AreExpectedMetricsGenerated(testMetricsLogger);

            Assert.Equal(_hostJsonWithWorkFlowBundle, File.ReadAllText(_hostJsonFile));

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
            _options.IsFileSystemReadOnly = true;

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
                    },
                    'applicationInsights': {
                        'prop': 'Hey=AS1$@%#$%W-k2j"";SharedAccessKey=foo,Data Source=barzons,Server=bathouse""testing',
                        'values': [ 'plain', 10, 'Password=hunter2' ],
                        'sampleSettings': {
                            'my-password': 'hunter2',
                            'service_token': 'token',
                            'StorageSas': 'access'
                        }
                    },
                    'prop': 'Hey=AS1$@%#$%W-k2j"";SharedAccessKey=foo,Data Source=barzons,Server=bathouse""testing',
                    'values': [ 'plain', 10, 'Password=hunter2' ],
                    'my-password': 'hunter2',
                    'service_token': 'token',
                    'StorageSas': 'access',
                    'aSecret': { 'value1': 'value' }
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
                    },
                    'applicationInsights': {
                        'prop': 'Hey=AS1$@%#$%W-k2j"";[Hidden Credential]""testing',
                        'values': [ 'plain', 10, '[Hidden Credential]' ],
                        'sampleSettings': {
                            'my-password': '[Hidden Credential]',
                            'service_token': '[Hidden Credential]',
                            'StorageSas': '[Hidden Credential]'
                        }
                    },
                    'prop': 'Hey=AS1$@%#$%W-k2j"";[Hidden Credential]""testing',
                    'values': [ 'plain', 10, '[Hidden Credential]' ],
                    'my-password': '[Hidden Credential]',
                    'service_token': '[Hidden Credential]',
                    'StorageSas': '[Hidden Credential]',
                    'aSecret': '[Hidden Credential]'
                }
            }";

            // for formatting
            var hostJson = JObject.Parse(hostJsonSanitized);
            var logger = _loggerProvider.CreatedLoggers.Single(l => l.Category == LogCategories.Startup);
            var logMessage = logger.GetLogMessages().Single(l => l.FormattedMessage.StartsWith("Host configuration file read")).FormattedMessage;
            Assert.Equal($"Host configuration file read:{Environment.NewLine}{hostJson}", logMessage);
        }

        [Fact]
        public void InvalidHostJsonLogsDiagnosticEvent()
        {
            Assert.False(File.Exists(_hostJsonFile));

            var hostJsonContent = " { fooBar";
            TestMetricsLogger testMetricsLogger = new TestMetricsLogger();

            File.WriteAllText(_hostJsonFile, hostJsonContent);
            Assert.True(File.Exists(_hostJsonFile));

            var ex = Assert.Throws<FormatException>(() => BuildHostJsonConfiguration(testMetricsLogger));

            var expectedTraceMessage = $"Unable to parse host configuration file '{_hostJsonFile}'.";

            LogMessage actualEvent = null;

            // Find the expected diagnostic event
            foreach (var message in _loggerProvider.GetAllLogMessages())
            {
                if (message.FormattedMessage.IndexOf(expectedTraceMessage, StringComparison.OrdinalIgnoreCase) > -1 &&
                    message.Level == LogLevel.Error &&
                    message.State is Dictionary<string, object> dictionary &&
                    dictionary.ContainsKey("MS_HelpLink") && dictionary.ContainsKey("MS_ErrorCode") &&
                    dictionary.GetValueOrDefault("MS_HelpLink").ToString().Equals(DiagnosticEventConstants.UnableToParseHostConfigurationFileHelpLink.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    dictionary.GetValueOrDefault("MS_ErrorCode").ToString().Equals(DiagnosticEventConstants.UnableToParseHostConfigurationFileErrorCode.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    actualEvent = message;
                    break;
                }
            }

            // Make sure that the expected event was found
            Assert.NotNull(actualEvent);
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
