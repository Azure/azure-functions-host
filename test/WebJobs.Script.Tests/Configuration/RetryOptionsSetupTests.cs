// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class RetryOptionsSetupTests
    {
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly string _hostJsonFile;
        private readonly string _rootPath;
        private readonly ScriptApplicationHostOptions _options;

        public RetryOptionsSetupTests()
        {
            _rootPath = Path.Combine(Environment.CurrentDirectory, "RetryOptionsSetupTests");
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

        [Fact]
        public void Retry_Config_FixedDelay_ExpectedValues()
        {
            string hostJsonContent = @"{
                'version': '2.0',
                'retry': {
                    'strategy': 'fixedDelay',
                    'maxRetryCount': '5',
                    'delayInterval': '00:00:01'
                    }
                }";

            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var configuration = BuildHostJsonConfiguration();
            RetryOptionsSetup setup = new RetryOptionsSetup(configuration);
            RetryOptions options = new RetryOptions();
            setup.Configure(options);

            Assert.Equal(options.Strategy, RetryStrategy.FixedDelay);
            Assert.Equal(options.MaxRetryCount.Value, 5);
            Assert.Equal(options.DelayInterval.Value, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Retry_Config_ExponentialBackoff_ExpectedValues()
        {
            string hostJsonContent = @"{
                'version': '2.0',
                'retry': {
                    'strategy': 'exponentialBackoff',
                    'maxRetryCount': '5',
                    'minimumInterval': '00:00:01',
                    'maximumInterval': '00:00:10'
                    }
                }";

            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var configuration = BuildHostJsonConfiguration();
            RetryOptionsSetup setup = new RetryOptionsSetup(configuration);
            RetryOptions options = new RetryOptions();
            setup.Configure(options);

            Assert.Equal(options.Strategy, RetryStrategy.ExponentialBackoff);
            Assert.Equal(options.MaxRetryCount.Value, 5);
            Assert.Equal(options.MinimumInterval.Value, TimeSpan.FromSeconds(1));
            Assert.Equal(options.MaximumInterval.Value, TimeSpan.FromSeconds(10));
        }

        [Theory]
        [InlineData(@"{
                'version': '2.0',
                'retry': {
                    'strategy': 'test',
                    'maxRetryCount': '5',
                    'delayInterval': '00:00:01'
                    }
                }")]
        [InlineData(@"{
                'version': '2.0',
                'retry': {
                    'strategy': 'fixedDelay',
                    'maxRetryCount': 'test',
                    'delayInterval': '00:00:01'
                    }
                }")]
        [InlineData(@"{
                'version': '2.0',
                'retry': {
                    'strategy': 'fixedDelay',
                    'maxRetryCount': '5',
                    'delayInterval': 'test'
                    }
                }")]
        [InlineData(@"{
                'version': '2.0',
                'retry': {
                    'strategy': 'exponentialBackoff',
                    'maxRetryCount': '5',
                    'minimumInterval': 'test',
                    'maximumInterval': '00:00:10'
                    }
                }")]
        public void Retry_Config_Throws_Exception(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var configuration = BuildHostJsonConfiguration();
            RetryOptionsSetup setup = new RetryOptionsSetup(configuration);
            RetryOptions options = new RetryOptions();
            Assert.Throws(typeof(InvalidOperationException), () =>
            {
                setup.Configure(options);
            });
        }

        private IConfiguration BuildHostJsonConfiguration(IEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var configSource = new HostJsonFileConfigurationSource(_options, environment, loggerFactory, new TestMetricsLogger());

            var configurationBuilder = new ConfigurationBuilder()
                .Add(configSource)
                .Add(new ScriptEnvironmentVariablesConfigurationSource());

            return configurationBuilder.Build();
        }
    }
}
