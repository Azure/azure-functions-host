// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        public async Task GetValue_ReloadsConfig_OnUpdate()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string testKey = "test_key";
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                File.WriteAllText(fileName, $"key1=value1,{testKey}=value2");

                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(100), TimeSpan.FromMilliseconds(100));
                Assert.Equal(conf.GetValue(testKey), "value2");

                File.WriteAllText(fileName, $"key1=value1,{testKey}=stamp");
                await Task.Delay(500);
                Assert.Equal(conf.GetValue(testKey), "stamp");

                File.WriteAllText(fileName, "key1=value1");
                await Task.Delay(500);
                Assert.Equal(conf.GetValue(testKey), null);

                Assert.True(_loggerProvider.GetAllLogMessages().Where(x => x.FormattedMessage.StartsWith("Updaiting FunctionsHostingConfigurations")).Count() == 3);
            }
        }

        [Theory]
        [InlineData("", "", false)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=stamp,flag2=value2", "", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=stamp app1,flag2=value2", "", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1 stamp,flag2=value2", "", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1,flag2=value2", "app1", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1 app2,flag2=value2", "app1", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1 app2,flag2=value2", "app2", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1 app2,flag2=value2", "app3", false)]
        public void FunctionsWorkerDynamicConcurrencyEnabled_ReturnsExpected(string config, string siteName, bool isEnabled)
        {
            TestEnvironment environment = new TestEnvironment();
            if (!string.IsNullOrEmpty(siteName))
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, siteName);
            }
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                File.WriteAllText(fileName, config);
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                conf.GetValue("test"); // to run Parse
                Assert.Equal(conf.FunctionsWorkerDynamicConcurrencyEnabled, isEnabled);
            }
        }

        [Fact]
        public void ParsesConfig()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                File.WriteAllText(fileName, "ENABLE_FEATUREX=1,A=B,TimeOut=123");
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                conf.GetValue("test"); // to run Parse
                Assert.Equal(3, conf.Config.Count);
                Assert.Equal("1", conf.GetValue("ENABLE_FEATUREX"));
                Assert.Equal("B", conf.GetValue("A"));
                Assert.Equal("123", conf.GetValue("TimeOut"));
                Assert.Equal("123", conf.GetValue("timeout")); // check case insensitive search
            }
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        [InlineData(" ", 0)]
        [InlineData("flag1=value1", 1)]
        [InlineData("x=y,", 1)]
        [InlineData("x=y,a=", 1)]
        [InlineData("abcd", 0)]
        [InlineData("flag1=test1;test2,flag2=test2", 2)]
        public void ReturnsExpectedConfig(string config, int configCount)
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                File.WriteAllText(fileName, config);
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                conf.GetValue("test"); // to run Parse
                Assert.True(conf.Config.Count == configCount);
            }
        }

        [Fact]
        public void GetValue_ConfigurationFileDoesNotExists_Logs()
        {
            FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(_environment, _loggerFactory, "test.txt", DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
            Assert.Null(conf.GetValue("test"));
            Assert.Contains("FunctionsHostingConfigurations file does not exist", _loggerProvider.GetAllLogMessages().Select(x => x.FormattedMessage));
        }

        [Fact]
        public void GetValue_ReturnsExpected()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                File.WriteAllText(fileName, "flag1=value1,flag2=value2,flag3=value3");
                FunctionsHostingConfiguration conf = new FunctionsHostingConfiguration(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                Assert.Equal(conf.GetValue("flag2"), "value2");
                Assert.DoesNotContain("FunctionsHostingConfigurations file does not exist", _loggerProvider.GetAllLogMessages().Select(x => x.FormattedMessage));
            }
        }
    }
}
