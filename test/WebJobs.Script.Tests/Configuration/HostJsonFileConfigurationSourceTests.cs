// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
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
        private readonly string _hostJsonWithBundles = "{\r\n  \"version\": \"2.0\",\r\n  \"isDefaultHostConfig\": true,\r\n  \"extensionBundle\": {\r\n    \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\",\r\n    \"version\": \"[4.*, 5.0.0)\"\r\n  }\r\n}";
        private readonly string _hostJsonWithWorkFlowBundle = "{\r\n  \"version\": \"2.0\",\r\n  \"isDefaultHostConfig\": true,\r\n  \"extensionBundle\": {\r\n    \"id\": \"Microsoft.Azure.Functions.ExtensionBundle.Workflows\",\r\n    \"version\": \"[1.*, 2.0.0)\"\r\n  }\r\n}";
        private readonly string _defaultHostJson = "{\r\n  \"version\": \"2.0\",\r\n  \"isDefaultHostConfig\": true\r\n}";
        private readonly ScriptApplicationHostOptions _options;
        private readonly string _hostJsonFile;
        private readonly TestLoggerProvider _loggerProvider = new();

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
        public void MissingHostJson_GeneratesExpectedDefaultConfigFile()
        {
            Assert.False(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new();

            BuildHostJsonConfiguration(testMetricsLogger);

            AreExpectedMetricsGenerated(testMetricsLogger);

            // verify actual file content
            Assert.Equal(_hostJsonWithBundles, File.ReadAllText(_hostJsonFile));

            // verify log messages
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Single(logs.Where(l => l.Level == LogLevel.Information && l.FormattedMessage == "No host configuration file found. Creating a default host.json file."));
            VerifySanitizedHostConfigLog(logs, ScriptConstants.DefaultExtensionBundleId, ScriptConstants.DefaultExtensionBundleVersion);
        }

        [Fact]
        public void MissingHostJson_WorkflowApp_GeneratesExpectedDefaultConfigFile()
        {
            var environment = new TestEnvironment
            {
                [EnvironmentSettingNames.AppKind] = "workflowApp"
            };

            Assert.False(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new();

            BuildHostJsonConfiguration(testMetricsLogger, environment);

            AreExpectedMetricsGenerated(testMetricsLogger);

            // verify actual file content
            Assert.Equal(_hostJsonWithWorkFlowBundle, File.ReadAllText(_hostJsonFile));

            // verify log messages
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Single(logs.Where(l => l.Level == LogLevel.Information && l.FormattedMessage == "No host configuration file found. Creating a default host.json file."));
            VerifySanitizedHostConfigLog(logs, ScriptConstants.WorkFlowExtensionBundleId, ScriptConstants.LogicAppDefaultExtensionBundleVersion);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\r\n}")]
        public void EmptyHostJson_CreatesDefaultFile(string json)
        {
            File.WriteAllText(_hostJsonFile, json);
            Assert.True(File.Exists(_hostJsonFile));
            TestMetricsLogger testMetricsLogger = new();

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
            StringBuilder hostJsonContentBuilder = new(@"{");
            hostJsonContentBuilder.Append(versionLine);
            hostJsonContentBuilder.Append(@"'functions': [ 'FunctionA', 'FunctionB' ]}");
            string hostJsonContent = hostJsonContentBuilder.ToString();

            TestMetricsLogger testMetricsLogger = new();

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

            TestEnvironment environment = new()
            {
                [EnvironmentSettingNames.AzureWebsiteZipDeployment] = "1"
            };

            TestMetricsLogger testMetricsLogger = new();
            IConfiguration config = BuildHostJsonConfiguration(testMetricsLogger, environment);
            AreExpectedMetricsGenerated(testMetricsLogger);
            var configList = config.AsEnumerable().ToList();
            Assert.Equal(config["AzureFunctionsJobHost:version"], "2.0");
            Assert.Equal(configList.Count, 4);
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
            TestMetricsLogger testMetricsLogger = new();

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

            string hostJsonContent = " { fooBar";
            TestMetricsLogger testMetricsLogger = new();

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

        [Fact]
        public void Load_WithProfile_FromEnvironment_ValuesIncluded()
        {
            HostConfigurationProfile profile = HostConfigurationProfile.Get("mcp-custom-handler");
            TestEnvironment environment = new()
            {
                ["AzureFunctionsJobHost__configurationProfile"] = "mcp-custom-handler",
            };

            IConfiguration config = BuildHostJsonConfiguration(new TestMetricsLogger(), environment);

            Assert.Equal("mcp-custom-handler", config["AzureFunctionsJobHost:configurationProfile"]);

            foreach ((string key, string value) in profile.Configuration)
            {
                string path = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, key);
                Assert.Equal(value, config[path]);
            }
        }

        [Fact]
        public void Load_WithProfile_FromJson_ValuesIncluded()
        {
            HostConfigurationProfile profile = HostConfigurationProfile.Get("mcp-custom-handler");
            string json = """
            {
                "version": "2.0",
                "configurationProfile": "mcp-custom-handler"
            }
            """;

            File.WriteAllText(_hostJsonFile, json);
            IConfiguration config = BuildHostJsonConfiguration(new TestMetricsLogger(), new TestEnvironment());
            Assert.Equal("mcp-custom-handler", config["AzureFunctionsJobHost:configurationProfile"]);

            foreach ((string key, string value) in profile.Configuration)
            {
                string path = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, key);
                Assert.Equal(value, config[path]);
            }
        }

        [Fact]
        public void Load_WithProfile_ValuesOverridden()
        {
            HostConfigurationProfile profile = HostConfigurationProfile.Get("mcp-custom-handler");
            string keyToOverride = profile.Configuration
                .First(x => x.Key != "configurationProfile").Key;
            string overrideValue = Guid.NewGuid().ToString();
            string json = $$"""
            {
                "version": "2.0",
                "configurationProfile": "mcp-custom-handler",
                "{{keyToOverride}}": "{{overrideValue}}"
            }
            """;

            File.WriteAllText(_hostJsonFile, json);
            IConfiguration config = BuildHostJsonConfiguration(new TestMetricsLogger(), new TestEnvironment());
            Assert.Equal("mcp-custom-handler", config["AzureFunctionsJobHost:configurationProfile"]);
            foreach ((string key, string value) in profile.Configuration)
            {
                string expected = key == keyToOverride ? overrideValue : value;
                string path = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, key);
                Assert.Equal(expected, config[path]);
            }
        }

        private IConfiguration BuildHostJsonConfiguration(TestMetricsLogger testMetricsLogger, IEnvironment environment = null)
        {
            environment ??= new TestEnvironment();
            LoggerFactory loggerFactory = new();
            loggerFactory.AddProvider(_loggerProvider);

            HostJsonFileConfigurationOptions options = new(environment, _options);
            HostJsonFileConfigurationSource configSource = new(options, loggerFactory, testMetricsLogger);

            IConfigurationBuilder configurationBuilder = new ConfigurationBuilder().Add(configSource);
            return configurationBuilder.Build();
        }

        private bool AreExpectedMetricsGenerated(TestMetricsLogger metricsLogger)
        {
            return metricsLogger.EventsBegan.Contains(MetricEventNames.LoadHostConfigurationSource) && metricsLogger.EventsEnded.Contains(MetricEventNames.LoadHostConfigurationSource)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.LoadHostConfiguration) && metricsLogger.EventsEnded.Contains(MetricEventNames.LoadHostConfiguration)
                    && metricsLogger.EventsBegan.Contains(MetricEventNames.InitializeHostConfiguration) && metricsLogger.EventsEnded.Contains(MetricEventNames.InitializeHostConfiguration);
        }

        private static void VerifySanitizedHostConfigLog(IList<LogMessage> logs, string expectedBundleId, string expectedBundleVersionSpec)
        {
            var hostJsonLog = logs.Single(p => p.EventId.Name == "HostConfigRead");
            Assert.Equal(LogLevel.Information, hostJsonLog.Level);
            string sanitizedJson = (string)hostJsonLog.State.ToDictionary()["sanitizedJson"];
            var jo = JObject.Parse(sanitizedJson);

            Assert.Equal(3, jo.Count);
            Assert.Equal("2.0", jo["version"]);
            Assert.Equal(true, jo["isDefaultHostConfig"]);

            var bundleConfig = jo["extensionBundle"];
            Assert.Equal(expectedBundleId, bundleConfig["id"]);
            Assert.Equal(expectedBundleVersionSpec, bundleConfig["version"]);
        }
    }
}
