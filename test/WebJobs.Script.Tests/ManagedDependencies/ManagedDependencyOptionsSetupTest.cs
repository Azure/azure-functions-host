// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ManagedDependencies
{
    public class ManagedDependencyOptionsSetupTest
    {
        private readonly ScriptApplicationHostOptions _options;
        private readonly string _hostJsonFilePath;
        private readonly TestLoggerProvider _loggerProvider;

        public ManagedDependencyOptionsSetupTest()
        {
            _loggerProvider = new TestLoggerProvider();

            var scriptPath = Path.Combine(Path.GetTempPath(), "managed_dependency_test");
            if (!Directory.Exists(scriptPath))
            {
                Directory.CreateDirectory(scriptPath);
            }

            _hostJsonFilePath = Path.Combine(scriptPath, "host.json");
            if (File.Exists(_hostJsonFilePath))
            {
                File.Delete(_hostJsonFilePath);
            }

            _options = new ScriptApplicationHostOptions
            {
                ScriptPath = scriptPath
            };
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0'
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'managedDependency':{}
                        }")]
        public void Test_ManagedDependencyOptionsSetup_Empty_Or_No_ManagedDependencies_InHostJson(string hostJson)
        {
            File.WriteAllText(_hostJsonFilePath, hostJson);
            Assert.True(File.Exists(_hostJsonFilePath));
            var managedDependencyOptions = new ManagedDependencyOptions();
            var configuration = BuildHostJsonConfiguration();
            ManagedDependencyOptionsSetup managedDependencyOptionsSetup = new ManagedDependencyOptionsSetup(configuration);
            managedDependencyOptionsSetup.Configure(managedDependencyOptions);
            Assert.True(managedDependencyOptions.Enabled == false);
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    'managedDependency':
                          {
                            'enabled': true
                          }
                        }")]
        [InlineData(@"{
                    'version': '2.0',
                    'managedDependency':
                          {
                            'enabled': false
                          }
                        }")]
        public void Test_ManagedDependencyOptionsSetup_Valid_ManagedDependencies_InHostFile(string hostJson)
        {
            File.WriteAllText(_hostJsonFilePath, hostJson);
            Assert.True(File.Exists(_hostJsonFilePath));

            var managedDependencyOptions = new ManagedDependencyOptions();
            var configuration = BuildHostJsonConfiguration();
            ManagedDependencyOptionsSetup managedDependencyOptionsSetup = new ManagedDependencyOptionsSetup(configuration);
            managedDependencyOptionsSetup.Configure(managedDependencyOptions);
            if (managedDependencyOptions.Enabled)
            {
                Assert.True(true);
            }
            else
            {
                Assert.True(managedDependencyOptions.Enabled == false);
            }
        }

        private IConfiguration BuildHostJsonConfiguration(IEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var configSource = new HostJsonFileConfigurationSource(_options, environment, loggerFactory, new TestMetricsLogger());

            var configurationBuilder = new ConfigurationBuilder()
                .Add(configSource);

            return configurationBuilder.Build();
        }
    }
}
