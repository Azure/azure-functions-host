// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class FunctionsHostingConfigurationsTest
    {
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory;

        public FunctionsHostingConfigurationsTest()
        {
            _environment = new TestEnvironment();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_loggerProvider);
        }

        [Fact]
        public void ScmHostingConfigurations_Registered_ByDefault()
        {
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            var conf = host.Services.GetService<IFunctionsHostingConfigurations>();
            Assert.NotNull(conf);
        }

        [Fact]
        public void FunctionsWorkerDynamicConcurrencyEnabled_Throws_InvalidOperationException()
        {
            FunctionsHostingConfigurations conf = new FunctionsHostingConfigurations (_environment, _loggerFactory, "C:\\somedir\test.txt", DateTime.Now.AddMilliseconds(1), TimeSpan.FromMinutes(5));
            Assert.Throws<InvalidOperationException>(() => conf.FunctionsWorkerDynamicConcurrencyEnabled);
        }

        [Fact]
        public async Task FunctionsWorkerDynamicConcurrencyEnabled_UpdatesSettings()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                File.WriteAllText(fileName, $"key1=value1,{RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled}=value2");

                FunctionsHostingConfigurations conf = new FunctionsHostingConfigurations (_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(100), TimeSpan.FromMilliseconds(100));
                Assert.False(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                File.WriteAllText(fileName, $"key1=value1,{RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled}=stamp");
                await Task.Delay(500);
                Assert.True(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                File.WriteAllText(fileName, "key1=value1");
                await Task.Delay(500);
                Assert.False(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                Assert.True(_loggerProvider.GetAllLogMessages().Where(x => x.FormattedMessage.StartsWith("Updaiting FunctionsHostingConfigurations")).Count() == 3);
            }
        }

        [Theory]
        //[InlineData("", "", false)]
        //[InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=stamp,flag2=value2", "", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1,app2,flag2=value2", "app1", true)]
        [InlineData("flag1=value1,FUNCTIONS_WORKER_DYNAMIC_CONCURRENCY_ENABLED=app1,app2,flag2=value2", "app3", false)]
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
                FunctionsHostingConfigurations conf = new FunctionsHostingConfigurations(environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
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
                FunctionsHostingConfigurations conf = new FunctionsHostingConfigurations(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                conf.GetValue("test"); // to run Parse
                Assert.Equal(3, conf.Config.Count);
                Assert.Equal("1", conf.GetValue("ENABLE_FEATUREX"));
                Assert.Equal("B", conf.GetValue("A"));
                Assert.Equal("123", conf.GetValue("TimeOut"));
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
                FunctionsHostingConfigurations conf = new FunctionsHostingConfigurations(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                conf.GetValue("test"); // to run Parse
                Assert.True(conf.Config.Count == configCount);
            }
        }
    }
}
