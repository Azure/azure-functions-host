// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class CustomHttpHeadersOptionsSetupTests
    {
        private readonly TestEnvironment _environment = new TestEnvironment();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly string _hostJsonFile;
        private readonly string _rootPath;
        private readonly ScriptApplicationHostOptions _options;

        public CustomHttpHeadersOptionsSetupTests()
        {
            _rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            Environment.SetEnvironmentVariable(AzureWebJobsScriptRoot, _rootPath);

            if (!Directory.Exists(_rootPath))
            {
                Directory.CreateDirectory(_rootPath);
            }

            _options = new ScriptApplicationHostOptions
            {
                ScriptPath = _rootPath
            };

            _hostJsonFile = Path.Combine(_rootPath, "host.json");
            if (File.Exists(_hostJsonFile))
            {
                File.Delete(_hostJsonFile);
            }
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'http': {
                        'customHeaders': {
                            'X-Content-Type-Options': 'nosniff'
                            }
                        }
                    }")]
        public void MissingOrValidCustomHttpHeadersConfig_DoesNotThrowException(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();

            CustomHttpHeadersOptionsSetup setup = new CustomHttpHeadersOptionsSetup(configuration);
            CustomHttpHeadersOptions options = new CustomHttpHeadersOptions();
            var ex = Record.Exception(() => setup.Configure(options));
            Assert.Null(ex);
        }

        [Fact]
        public void ValidCustomHttpHeadersConfig_BindsToOptions()
        {
            string hostJsonContent = @"{
                                         'version': '2.0',
                                         'http': {
                                             'customHeaders': {
                                                 'X-Content-Type-Options': 'nosniff'
                                             }
                                         }
                                     }";
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();

            CustomHttpHeadersOptionsSetup setup = new CustomHttpHeadersOptionsSetup(configuration);
            CustomHttpHeadersOptions options = new CustomHttpHeadersOptions();
            setup.Configure(options);
            Assert.Equal(new Dictionary<string, string>() { { "X-Content-Type-Options", "nosniff" } }, options);
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
