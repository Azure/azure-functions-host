// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class FunctionsHostingConfigurationTest
    {
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory;

        public FunctionsHostingConfigurationTest()
        {
            _environment = new TestEnvironment();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_loggerProvider);
        }

        [Fact]
        public void FunctionsHostingConfiguration_Registered_ByDefault()
        {
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            var conf = host.Services.GetService<IFunctionsHostingConfiguration>();
            Assert.NotNull(conf);
        }

        [Fact]
        public void Initialization_ConfigurationFileDoesNotExists()
        {
            TestEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, "unknown.txt");
            FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(environment, _loggerFactory/*"test.txt"/*, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100)*/);
            TestHelpers.Await(() =>
            {
                return conf.IsInitialized;
            });
            Assert.Contains("FunctionsHostingConfiguration file does not exist.", _loggerProvider.GetAllLogMessages().Select(x => x.FormattedMessage));
        }

        public void Initialized_Fires()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                TestEnvironment environment = new TestEnvironment();
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, fileName);
                File.WriteAllText(fileName, "feature1=1");
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(environment, _loggerFactory, fileName);
                bool fires = false;
                conf.Initialized += (s, e) =>
                {
                    fires = true;
                };
                TestHelpers.Await(() =>
                {
                    return fires;
                });
                Assert.True(conf.IsInitialized);
            }
        }

        private void Conf_Initialized(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void GetValue_LogsError_IfNotInitialized()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                TestEnvironment environment = new TestEnvironment();
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, fileName);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
                File.WriteAllText(fileName, "feature1=1");
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(environment, _loggerFactory, fileName);

                Assert.Null(conf.GetFeatureFlag("feature1"));
                Assert.Contains("FunctionsHostingConfigurations haven't initialized on getting 'feature1'",
                    _loggerProvider.GetAllLogMessages().Select(x => x.FormattedMessage));
            }
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        [InlineData(" ", 0)]
        [InlineData("flag1=value1", 1)]
        [InlineData("flag1=value1,", 1)]
        [InlineData("flag1=value1,a=", 1)]
        [InlineData("abcd", 0)]
        [InlineData("flag1=value1;test2,flag2=value2", 2)]
        public void GetValue_ReturnsExpected(string config, int expectedCount)
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                TestEnvironment environment = new TestEnvironment();
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, fileName);
                File.WriteAllText(fileName, config);
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(environment, _loggerFactory, fileName);
                TestHelpers.Await(() =>
                {
                    return conf.IsInitialized;
                });

                Assert.True(conf.GetFeatureFlags().Count == expectedCount);
                var value = conf.GetFeatureFlag("flag1");
                if (value != null)
                {
                    Assert.Equal(value, "value1");
                    Assert.Equal(conf.GetFeatureFlag("FLAG1"), "value1"); // check case insensitiveness
                }
                Assert.DoesNotContain("FunctionsHostingConfigurations file does not exist", _loggerProvider.GetAllLogMessages().Select(x => x.FormattedMessage));
            }
        }
    }
}
