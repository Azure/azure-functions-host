﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HostJsonFileConfigurationSourceTests
    {
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
            BuildHostJsonConfiguration();

            Assert.Equal(_defaultHostJson, File.ReadAllText(_hostJsonFile));

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
            BuildHostJsonConfiguration();

            Assert.Equal(_defaultHostJson, File.ReadAllText(_hostJsonFile));

            var log = _loggerProvider.GetAllLogMessages().Single(l => l.FormattedMessage == "Empty host configuration file found. Creating a default host.json file.");
            Assert.Equal(LogLevel.Information, log.Level);
        }

        [Fact]
        public void MissingVersion_ThrowsException()
        {
            string hostJsonContent = @"
            {
              'functions': [ 'FunctionA', 'FunctionB' ]
            }";

            File.WriteAllText(_hostJsonFile, hostJsonContent);
            Assert.True(File.Exists(_hostJsonFile));

            var ex = Assert.Throws<HostConfigurationException>(() => BuildHostJsonConfiguration());
            Assert.StartsWith("The host.json file is missing the required 'version' property.", ex.Message);
        }

        [Fact]
        public void ReadOnlyFileSystem_SkipsDefaultHostJsonCreation()
        {
            Assert.False(File.Exists(_hostJsonFile));

            var environment = new TestEnvironment(new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "1" }
            });

            IConfiguration config = BuildHostJsonConfiguration(environment);
            Assert.Equal(config["AzureFunctionsJobHost:version"], "2.0");

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

            File.WriteAllText(_hostJsonFile, hostJsonContent);

            BuildHostJsonConfiguration();

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

        [Fact]
        public void Parse_Json_Token_For_NullOrEmptyString()
        {
            //  Invalid empty string as value
            string hostJsonContent = @"
            {
                'version': '2.0',
                'functionTimeout': ''
            }";

            File.WriteAllText(_hostJsonFile, hostJsonContent);
            Assert.True(File.Exists(_hostJsonFile));

            var config = BuildHostJsonConfiguration();
            Assert.Equal(config["AzureFunctionsJobHost:functionTimeout"], string.Empty);

            // Valid null as value
            hostJsonContent = @"
            {
                'version': '2.0',
                'functionTimeout': null
            }";

            File.WriteAllText(_hostJsonFile, hostJsonContent);
            Assert.True(File.Exists(_hostJsonFile));
            config = BuildHostJsonConfiguration();
            Assert.Equal(config["AzureFunctionsJobHost:functionTimeout"], null);
        }

        private IConfiguration BuildHostJsonConfiguration(IEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var configSource = new HostJsonFileConfigurationSource(_options, environment, loggerFactory);

            var configurationBuilder = new ConfigurationBuilder()
                .Add(configSource);

            return configurationBuilder.Build();
        }
    }
}
